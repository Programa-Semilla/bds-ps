using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Routing;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 011 US4 (FR-052..FR-060) — projects the reviewer queue. The Aging KPI
/// uses spec-010's <c>AgingThresholdDays</c> SystemConfiguration as the single
/// source of truth (FR-053, SC-010).
///
/// Spec 016 — every fetch composes an <see cref="IReviewerScope"/> so the
/// queue, signing inbox, and search apply the group-overlap predicate at the
/// EF query level (NFR-001). FR-014 adds a search-term parameter.
/// </summary>
public interface IReviewerQueueProjection
{
    Task<ReviewerQueueDto> GetForReviewerAsync(
        string reviewerId,
        string firstName,
        ReviewerFilter filter,
        IReviewerScope scope,
        string? searchTerm,
        CancellationToken ct);

    Task<IReadOnlyList<ReviewerQueueRowDto>> GetRowsAsync(
        string reviewerId,
        ReviewerFilter filter,
        IReviewerScope scope,
        string? searchTerm,
        CancellationToken ct);
}

public sealed class ReviewerQueueProjection : IReviewerQueueProjection
{
    private const int DefaultAgingThresholdDays = 7;
    private const string AgingKey = "AgingThresholdDays";

    private readonly IApplicationRepository _applications;
    private readonly ISystemConfigurationRepository _config;
    private readonly IJourneyProjector _journey;
    private readonly IReviewerCopyProvider _copy;

    public ReviewerQueueProjection(
        IApplicationRepository applications,
        ISystemConfigurationRepository config,
        IJourneyProjector journey,
        IReviewerCopyProvider copy)
    {
        _applications = applications;
        _config = config;
        _journey = journey;
        _copy = copy;
    }

    public async Task<ReviewerQueueDto> GetForReviewerAsync(
        string reviewerId,
        string firstName,
        ReviewerFilter filter,
        IReviewerScope scope,
        string? searchTerm,
        CancellationToken ct)
    {
        // Spec 011 v1 NOTE: per FR-069, no reviewer-assignment surface ships in v1
        // (no schema change FR-067, no per-reviewer ownership column on Application).
        // The platform's pre-existing model is "every Reviewer can view every
        // UnderReview item"; this projection inherits that contract. Spec 016
        // narrows that contract to "every Reviewer can view every UnderReview
        // item whose applicant shares at least one group" via the EF predicate
        // composed below. Admin callers short-circuit (FR-015).
        var threshold = await GetAgingThresholdAsync();
        var hint = new ReviewerScopeHint(scope.IsAdmin, scope.GroupIds);
        // Submitted apps haven't been opened yet; they must surface here so reviewers
        // can pick them up. The pre-spec-011 ReviewService.GetReviewQueueAsync also
        // queried Submitted — the queue dashboard needs the same scope to avoid
        // hiding work that has not yet transitioned to UnderReview.
        var submitted   = await _applications.GetByStateForReviewerAsync(ApplicationState.Submitted, hint, 1, 200, searchTerm);
        var underReview = await _applications.GetByStateForReviewerAsync(ApplicationState.UnderReview, hint, 1, 200, searchTerm);
        // Spec 040 — the auditor can return an application to the reviewer for rework
        // (ReturnedFromAudit). It is back in the reviewer's court (rework + re-send to
        // audit), so it must surface on the queue alongside Submitted/UnderReview;
        // otherwise the reviewer gets the email but never sees it on the dashboard.
        var returned    = await _applications.GetByStateForReviewerAsync(ApplicationState.ReturnedFromAudit, hint, 1, 200, searchTerm);
        var resolved    = await _applications.GetByStateForReviewerAsync(ApplicationState.Resolved, hint, 1, 200, searchTerm);

        var allCandidates = submitted.Items.Concat(underReview.Items).Concat(returned.Items).Concat(resolved.Items).ToList();
        var now = DateTimeOffset.UtcNow;

        // Counts (filter-independent). "Awaiting your review" includes Submitted
        // (no reviewer has opened it), UnderReview that hasn't received a per-item
        // decision yet, and ReturnedFromAudit (awaiting the reviewer's rework).
        int awaiting = submitted.Items.Count
                     + underReview.Items.Count(a => a.VersionHistory.LastOrDefault()?.Action != "ReviewItem")
                     + returned.Items.Count;
        int inProgress = underReview.Items.Count(a => a.VersionHistory.Any(v => v.Action == "ReviewItem"));
        int aging = submitted.Items.Concat(underReview.Items).Concat(returned.Items)
            .Count(a => _journey.DaysInCurrentState(a, now) > threshold);
        int decidedThisMonth = resolved.Items.Count(a => a.UpdatedAt.Year == DateTime.UtcNow.Year && a.UpdatedAt.Month == DateTime.UtcNow.Month);

        var rows = await ProjectRowsAsync(allCandidates, filter, threshold, now);
        var recent = allCandidates
            .SelectMany(a => a.VersionHistory.Select(v => new ReviewerActivityEvent(
                Occurred: new DateTimeOffset(v.Timestamp, TimeSpan.Zero),
                Title: ActivityActionCopy.Title(v.Action),
                ApplicantName: FormatApplicantName(a.Applicant),
                ApplicationNumber: $"APP-{a.Id:D5}",
                DeepLinkHref: ReviewRoutes.DeepLinkFor(a.Id, v.Id))))
            .OrderByDescending(e => e.Occurred)
            .Take(5)
            .ToList();

        return new ReviewerQueueDto(
            FirstName: firstName,
            Kpis: new ReviewerKpiSnapshot(awaiting, inProgress, aging, decidedThisMonth),
            ActiveFilter: filter,
            RecentActivity: recent,
            HasMoreActivity: false,
            Rows: rows,
            AgingThresholdDays: threshold);
    }

    public async Task<IReadOnlyList<ReviewerQueueRowDto>> GetRowsAsync(
        string reviewerId,
        ReviewerFilter filter,
        IReviewerScope scope,
        string? searchTerm,
        CancellationToken ct)
    {
        var threshold = await GetAgingThresholdAsync();
        var hint = new ReviewerScopeHint(scope.IsAdmin, scope.GroupIds);
        var submitted   = await _applications.GetByStateForReviewerAsync(ApplicationState.Submitted, hint, 1, 200, searchTerm);
        var underReview = await _applications.GetByStateForReviewerAsync(ApplicationState.UnderReview, hint, 1, 200, searchTerm);
        var returned    = await _applications.GetByStateForReviewerAsync(ApplicationState.ReturnedFromAudit, hint, 1, 200, searchTerm);
        var resolved    = await _applications.GetByStateForReviewerAsync(ApplicationState.Resolved, hint, 1, 200, searchTerm);
        var all = submitted.Items.Concat(underReview.Items).Concat(returned.Items).Concat(resolved.Items).ToList();
        return await ProjectRowsAsync(all, filter, threshold, DateTimeOffset.UtcNow);
    }

    private Task<IReadOnlyList<ReviewerQueueRowDto>> ProjectRowsAsync(
        IReadOnlyList<AppEntity> apps,
        ReviewerFilter filter,
        int agingThresholdDays,
        DateTimeOffset now)
    {
        var filtered = filter switch
        {
            ReviewerFilter.AwaitingMe => apps.Where(a =>
                a.State == ApplicationState.Submitted
                || a.State == ApplicationState.UnderReview
                || a.State == ApplicationState.ReturnedFromAudit).ToList(),
            ReviewerFilter.Aging      => apps.Where(a => _journey.DaysInCurrentState(a, now) > agingThresholdDays).ToList(),
            ReviewerFilter.SentBack   => apps.Where(a => a.VersionHistory.Any(v => v.Action == "SendBack")).ToList(),
            ReviewerFilter.Appealing  => apps.Where(a => a.Appeals.Any(p => p.Status == AppealStatus.Open)).ToList(),
            _                         => apps.ToList(),
        };

        var microProjections = _journey.ProjectMany(filtered, JourneyVariant.Micro);
        var rows = filtered.Select(a =>
        {
            var micro = microProjections.TryGetValue(a.Id, out var jvm) ? jvm : _journey.Project(a, JourneyVariant.Micro);
            // Spec 015 / T414 — converted-CRC total + non-CRC flag for the row.
            var (totalCrc, hasNonCrc) = ApplicationCurrencyTotal.Compute(a);
            return new ReviewerQueueRowDto(
                ApplicationId: Guid.Empty,
                ApplicationNumber: $"APP-{a.Id:D5}",
                ProjectName: a.Items.FirstOrDefault()?.ProductName ?? $"Application #{a.Id}",
                ApplicantName: FormatApplicantName(a.Applicant),
                ApplicantAvatarUrl: null,
                JourneyMicro: micro,
                DaysInCurrentState: _journey.DaysInCurrentState(a, now),
                LastActivity: a.UpdatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(a.UpdatedAt, TimeSpan.Zero),
                PrimaryAction: new ContextualAction("Revisar", ReviewRoutes.PathFor(a.Id), ContextualActionStyle.Primary),
                TotalConvertedCrc: totalCrc,
                HasNonCrcQuotation: hasNonCrc);
        }).ToList();
        return Task.FromResult<IReadOnlyList<ReviewerQueueRowDto>>(rows);
    }

    private static string FormatApplicantName(Applicant? applicant)
    {
        if (applicant is null) return "Solicitante";
        var first = applicant.FirstName ?? string.Empty;
        var last = applicant.LastName ?? string.Empty;
        var full = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(full) ? "Solicitante" : full;
    }

    private async Task<int> GetAgingThresholdAsync()
    {
        try
        {
            var entry = await _config.GetByKeyAsync(AgingKey);
            if (entry is null) return DefaultAgingThresholdDays;
            return int.TryParse(entry.Value, out var v) ? v : DefaultAgingThresholdDays;
        }
        catch
        {
            return DefaultAgingThresholdDays;
        }
    }
}
