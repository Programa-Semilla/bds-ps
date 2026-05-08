using FundingPlatform.Application.DTOs;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 011 US1 (FR-024..FR-033) — projects an applicant's portfolio into the
/// home-dashboard view model. Reads only existing aggregates (no schema changes).
/// </summary>
public interface IApplicantDashboardProjection
{
    Task<ApplicantDashboardDto> GetForUserAsync(int applicantId, string firstName, CancellationToken ct);
}

public sealed class ApplicantDashboardProjection : IApplicantDashboardProjection
{
    private readonly IApplicationRepository _applications;
    private readonly IJourneyProjector _journey;
    private readonly IApplicantCopyProvider _copy;

    public ApplicantDashboardProjection(
        IApplicationRepository applications,
        IJourneyProjector journey,
        IApplicantCopyProvider copy)
    {
        _applications = applications;
        _journey = journey;
        _copy = copy;
    }

    public async Task<ApplicantDashboardDto> GetForUserAsync(int applicantId, string firstName, CancellationToken ct)
    {
        var apps = await _applications.GetForApplicantDashboardAsync(applicantId);
        var now = DateTimeOffset.UtcNow;

        // KPI counts
        int active = 0, inReview = 0, awaiting = 0, funded = 0;
        foreach (var a in apps)
        {
            if (a.State == ApplicationState.AgreementExecuted) funded++;
            else if (a.State == ApplicationState.UnderReview) { active++; inReview++; }
            else if (a.State != ApplicationState.Resolved) active++;

            if (a.State == ApplicationState.Draft || NeedsApplicantAction(a)) awaiting++;
        }

        // Awaiting-action highest priority surface (one only).
        AwaitingAction? awaitingAction = null;
        foreach (var a in apps.OrderByDescending(x => x.UpdatedAt))
        {
            if (a.FundingAgreement is not null && a.State != ApplicationState.AgreementExecuted)
            {
                awaitingAction = new AwaitingAction(
                    Guid.Empty,
                    $"APP-{a.Id:D5}",
                    GetProjectName(a),
                    _copy.AwaitingActionAgreement(GetProjectName(a)),
                    _copy.ActionSignAgreement(),
                    AgreementDetailsUrl(a.Id));
                break;
            }
            if (NeedsApplicantAction(a))
            {
                awaitingAction = new AwaitingAction(
                    Guid.Empty,
                    $"APP-{a.Id:D5}",
                    GetProjectName(a),
                    _copy.AwaitingActionSentBack(GetProjectName(a)),
                    _copy.ActionAddMissingDetails(),
                    $"/Application/Details/{a.Id}");
                break;
            }
            if (a.State == ApplicationState.Draft)
            {
                awaitingAction = new AwaitingAction(
                    Guid.Empty,
                    $"APP-{a.Id:D5}",
                    GetProjectName(a),
                    _copy.AwaitingActionDraft(GetProjectName(a)),
                    _copy.ActionContinueApplication(),
                    $"/Application/Details/{a.Id}");
                break;
            }
        }

        var miniProjections = _journey.ProjectMany(apps, JourneyVariant.Mini);
        const int CardLimit = 3;
        var ordered = apps
            .Where(a => a.State != ApplicationState.AgreementExecuted)
            .OrderByDescending(a => a.UpdatedAt)
            .ToList();
        var visibleCards = ordered.Take(CardLimit).Select(a =>
        {
            var mini = miniProjections.TryGetValue(a.Id, out var jvm)
                ? jvm
                : _journey.Project(a, JourneyVariant.Mini);
            var stageMapping = mini.Mainline.FirstOrDefault(n => n.State == JourneyNodeState.Current)?.Label
                ?? mini.Mainline.LastOrDefault(n => n.State == JourneyNodeState.Completed)?.Label
                ?? "Draft";
            // Spec 015 / T414 — surface the per-application converted-CRC total + a
            // non-CRC presence flag so the dashboard card can hint at multi-currency
            // composition.
            var (totalCrc, hasNonCrc) = ApplicationCurrencyTotal.Compute(a);
            return new ApplicationCardDto(
                ApplicationId: Guid.Empty,
                ApplicationNumber: $"APP-{a.Id:D5}",
                ProjectName: GetProjectName(a),
                JourneyMini: mini,
                CurrentStageLabel: stageMapping,
                DaysInCurrentState: _journey.DaysInCurrentState(a, now),
                LastActivity: a.UpdatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(a.UpdatedAt, TimeSpan.Zero),
                PrimaryAction: ResolveContextualAction(a),
                TotalConvertedCrc: totalCrc,
                HasNonCrcQuotation: hasNonCrc);
        }).ToList();

        var recent = apps
            .SelectMany(a => a.VersionHistory.Select(v => new ActivityEvent(
                Occurred: new DateTimeOffset(v.Timestamp, TimeSpan.Zero),
                Title: PrettyAction(v.Action),
                ActorName: v.UserId,
                DeepLinkHref: $"/Application/Details/{a.Id}#event-{v.Id}",
                IconKey: "ti ti-point")))
            .OrderByDescending(e => e.Occurred)
            .Take(10)
            .ToList();

        return new ApplicantDashboardDto(
            FirstName: firstName,
            Kpis: new KpiSnapshot(active, inReview, awaiting, funded),
            AwaitingAction: awaitingAction,
            ActiveApplications: visibleCards,
            RecentActivity: recent,
            HasMoreActivity: apps.SelectMany(a => a.VersionHistory).Count() > recent.Count,
            HasMoreApplications: ordered.Count > CardLimit);
    }

    private static bool NeedsApplicantAction(AppEntity a)
    {
        // Spec 011 awaiting-action surfaces: latest VersionHistory action == "SendBack" implies action required.
        var last = a.VersionHistory.OrderByDescending(v => v.Timestamp).FirstOrDefault();
        return last is not null && string.Equals(last.Action, "SendBack", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProjectName(AppEntity a)
    {
        // Spec-008 surfaces don't store a project name on Application directly;
        // the application detail page derives one from the first item's description.
        return a.Items.FirstOrDefault()?.ProductName ?? $"Application #{a.Id}";
    }

    private ContextualAction ResolveContextualAction(AppEntity a)
    {
        if (a.FundingAgreement is not null && a.State != ApplicationState.AgreementExecuted)
        {
            return new ContextualAction(_copy.ActionSignAgreement(), AgreementDetailsUrl(a.Id), ContextualActionStyle.Primary);
        }
        if (a.State == ApplicationState.Draft)
        {
            return new ContextualAction(_copy.ActionContinueApplication(), $"/Application/Details/{a.Id}", ContextualActionStyle.Primary);
        }
        return new ContextualAction(_copy.ActionOpenApplication(), $"/Application/Details/{a.Id}", ContextualActionStyle.Secondary);
    }

    /// <summary>
    /// Canonical URL for <c>FundingAgreementController.Details</c>. The controller
    /// is attribute-routed under <c>Applications/{applicationId:int}/FundingAgreement</c>
    /// with <c>[HttpGet("")]</c> on Details, so the right path is
    /// <c>/Applications/{id}/FundingAgreement</c> — not the historical
    /// <c>/FundingAgreement/Details/{id}</c> shape, which 404s.
    /// </summary>
    private static string AgreementDetailsUrl(int applicationId)
        => $"/Applications/{applicationId}/FundingAgreement";

    private static string PrettyAction(string action) => action switch
    {
        "Created"            => "Application created",
        "Submitted"          => "Application sent",
        "StartReview"        => "Review started",
        "SendBack"           => "Sent back for more details",
        "Finalize"           => "Decision recorded",
        "AgreementGenerated" => "Agreement generated",
        "AgreementExecuted"  => "Agreement signed",
        _                    => action,
    };
}
