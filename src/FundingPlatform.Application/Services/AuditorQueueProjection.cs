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
        var (items, _) = await _applications.GetByStateForReviewerAsync(
            ApplicationState.PendingAudit, hint, page, pageSize, searchTerm);

        return items.Select(Project).ToList();
    }

    private static AuditInboxRowDto Project(AppEntity a)
    {
        var name = a.Applicant is null
            ? "Solicitante"
            : $"{a.Applicant.FirstName} {a.Applicant.LastName}".Trim();
        if (string.IsNullOrEmpty(name)) name = "Solicitante";

        return new AuditInboxRowDto(
            ApplicationId: a.Id,
            ApplicantDisplayName: name,
            PublicCode: a.PublicCode?.Value,
            // Spec 040 — entered-audit time proxied by UpdatedAt (stamped on the
            // SendToAudit transition); the reviewer-queue load does not hydrate
            // VersionHistory. The detail page surfaces full provider compliance.
            EnteredAuditAtUtc: a.UpdatedAt,
            ItemCount: a.Items.Count,
            HasProviderWarning: false);
    }
}
