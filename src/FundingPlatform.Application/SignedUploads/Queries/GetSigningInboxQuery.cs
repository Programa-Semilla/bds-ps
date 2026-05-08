namespace FundingPlatform.Application.SignedUploads.Queries;

/// <summary>
/// Spec 016 — adds <c>ReviewerGroupIds</c> so the inbox query can compose the
/// group-overlap predicate at the EF query level (NFR-001). Admin callers
/// short-circuit via <see cref="IsAdministrator"/> (FR-015).
/// </summary>
public record GetSigningInboxQuery(
    string CurrentUserId,
    bool IsAdministrator,
    IReadOnlyCollection<int> ReviewerGroupIds,
    int Page,
    int PageSize);
