namespace FundingPlatform.Application.Reviewer;

/// <summary>
/// Spec 016 — request-scoped reviewer scope. Composes with EF queries to apply
/// the group-overlap predicate (NFR-001) and with detail-page authorization
/// (NFR-002). Admin callers short-circuit the predicate via <see cref="IsAdmin"/>.
/// </summary>
public interface IReviewerScope
{
    /// <summary>Admin callers see every applicant + every application
    /// (FR-015). The predicate is short-circuited when this is true.</summary>
    bool IsAdmin { get; }

    /// <summary>The signed-in reviewer's group ids. Empty when the reviewer has
    /// no memberships — the queue is empty and detail-page access denied
    /// (FR-005, FR-012).</summary>
    IReadOnlyCollection<int> GroupIds { get; }
}

/// <summary>
/// Spec 016 — provider that resolves an <see cref="IReviewerScope"/> from the
/// current HTTP principal + database. Scoped per request (NFR-003).
/// </summary>
public interface IReviewerScopeProvider
{
    /// <summary>Returns the scope for the given user id, or <see cref="Empty"/>
    /// if the user has no memberships and is not an admin.</summary>
    Task<IReviewerScope> GetForUserAsync(string userId, bool isAdmin, CancellationToken ct);
}

/// <summary>Default <see cref="IReviewerScope"/> record carrying the
/// admin-flag + group ids.</summary>
public sealed record ReviewerScope(bool IsAdmin, IReadOnlyCollection<int> GroupIds) : IReviewerScope
{
    /// <summary>The empty (zero-group, non-admin) scope.</summary>
    public static IReviewerScope Empty { get; } = new ReviewerScope(false, Array.Empty<int>());

    /// <summary>The admin scope — bypasses every predicate.</summary>
    public static IReviewerScope Admin { get; } = new ReviewerScope(true, Array.Empty<int>());
}
