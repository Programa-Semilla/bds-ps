using FundingPlatform.Application.Reviewer;

namespace FundingPlatform.Application.Audit;

/// <summary>
/// Spec 040 / D7 — the group-scoped auditor inbox of <c>PendingAudit</c> applications.
/// Mirrors the reviewer queue: the scope is resolved via
/// <see cref="IReviewerScopeProvider"/> (group ids by <c>UserGroupMembership</c>, admin
/// short-circuits to all); an auditor with no memberships sees an empty inbox.
/// <c>ReturnedFromAudit</c> apps are excluded (they sit with the reviewer).
/// </summary>
public interface IAuditorQueueProjection
{
    Task<IReadOnlyList<AuditInboxRowDto>> GetInboxAsync(
        IReviewerScope scope,
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken ct);
}
