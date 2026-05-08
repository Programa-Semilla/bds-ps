namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 017 / US1 — minimal user-store reader that the admin dashboard
/// projection consumes. Excludes the system sentinel admin per spec 009 FR-019.
/// </summary>
public interface IUserStoreReader
{
    /// <summary>
    /// Count of non-sentinel users whose accounts are active (no lockout in the
    /// future). Used by the "Active users" KPI on the admin dashboard.
    /// </summary>
    Task<int> GetActiveUserCountAsync(CancellationToken ct);

    /// <summary>
    /// Display name for an actor user id. Falls back to the id itself when no
    /// user is found (e.g. deleted accounts), per the activity feed graceful
    /// degradation contract.
    /// </summary>
    Task<string> GetDisplayNameAsync(string userId, CancellationToken ct);
}
