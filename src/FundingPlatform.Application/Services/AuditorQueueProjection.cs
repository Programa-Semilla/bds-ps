using FundingPlatform.Application.Audit;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 040 / D7 / T023 — the group-scoped auditor inbox of <c>PendingAudit</c>
/// applications. Reuses the reviewer-queue repository path
/// (<see cref="IApplicationRepository.GetByStateForReviewerAsync"/>) with the auditor's
/// own scope hint, so group-overlap (and the admin short-circuit) are enforced at the EF
/// query level exactly like the reviewer queue. <c>ReturnedFromAudit</c> apps are a
/// different state and are therefore naturally excluded.
/// </summary>
public sealed class AuditorQueueProjection : IAuditorQueueProjection
{
    private readonly IApplicationRepository _applications;

    public AuditorQueueProjection(IApplicationRepository applications)
    {
        _applications = applications;
    }

    public async Task<IReadOnlyList<AuditInboxRowDto>> GetInboxAsync(
        IReviewerScope scope, string? searchTerm, int page, int pageSize, CancellationToken ct)
    {
        var hint = new ReviewerScopeHint(scope.IsAdmin, scope.GroupIds);
        var (items, _) = await _applications.GetPendingAuditInboxAsync(hint, page, pageSize, searchTerm);
        return items.Select(Project).ToList();
    }

    private static readonly string[] SentToAuditActions = { "SentToAudit", "ResentToAudit" };

    private static AuditInboxRowDto Project(AppEntity a)
    {
        var name = a.Applicant is null
            ? "Solicitante"
            : $"{a.Applicant.FirstName} {a.Applicant.LastName}".Trim();
        if (string.IsNullOrEmpty(name)) name = "Solicitante";

        // Spec 040 / FR-006 — time the application entered audit = latest send/re-send
        // VersionHistory entry; falls back to UpdatedAt if none is loaded.
        var enteredAudit = a.VersionHistory
            .Where(v => SentToAuditActions.Contains(v.Action))
            .OrderByDescending(v => v.Timestamp)
            .Select(v => (DateTime?)v.Timestamp)
            .FirstOrDefault() ?? a.UpdatedAt;

        // Spec 040 / FR-006 — provider warning indicator: any item's selected (or any)
        // quotation supplier carries an admin-set regulatory warning (spec 038).
        var hasProviderWarning = a.Items
            .SelectMany(i => i.Quotations)
            .Any(q => q.Supplier is { HasWarning: true });

        return new AuditInboxRowDto(
            ApplicationId: a.Id,
            ApplicantDisplayName: name,
            PublicCode: a.PublicCode?.Value,
            EnteredAuditAtUtc: enteredAudit,
            ItemCount: a.Items.Count,
            HasProviderWarning: hasProviderWarning);
    }
}
