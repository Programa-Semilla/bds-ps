using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Abstractions.AiComparison;

/// <summary>
/// Spec 020 / data-model.md — persistence boundary for the cached artifact.
/// Authorization is NOT enforced here; callers apply the spec-016 group-overlap
/// predicate before invoking this repository.
/// </summary>
public interface IComparisonArtifactRepository
{
    Task<ComparisonArtifact?> GetByItemIdAsync(int applicationItemId, CancellationToken ct);

    /// <summary>Insert when no row exists for the item; otherwise replace in place.</summary>
    Task UpsertAsync(ComparisonArtifact artifact, CancellationToken ct);
}

/// <summary>Spec 020 / FR-F1..FR-F3 — queued-job persistence + status transitions.</summary>
public interface IComparisonJobRepository
{
    Task<ComparisonJob?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ComparisonJob>> GetPendingForApplicationAsync(int applicationId, CancellationToken ct);
    Task<ComparisonJob?> GetByApplicationItemAsync(int applicationItemId, CancellationToken ct);
    Task EnqueueAsync(ComparisonJob job, CancellationToken ct);
    Task UpdateAsync(ComparisonJob job, CancellationToken ct);

    /// <summary>Atomic Pending → Running claim. Returns null when nothing claimable.</summary>
    Task<ComparisonJob?> ClaimNextPendingAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>For the reaper — Running rows whose LastStatusChangeAt &lt; cutoff.</summary>
    Task<IReadOnlyList<ComparisonJob>> GetOrphanedRunningAsync(DateTimeOffset cutoff, CancellationToken ct);

    /// <summary>For status endpoint — latest job for a single item (any status).</summary>
    Task<ComparisonJob?> GetLatestByApplicationItemAsync(int applicationItemId, CancellationToken ct);
}
