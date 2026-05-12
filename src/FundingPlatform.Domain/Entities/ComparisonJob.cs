namespace FundingPlatform.Domain.Entities;

/// <summary>Spec 020 / data-model.md — comparison-job lifecycle.</summary>
public enum ComparisonJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// Spec 020 / FR-F1 — a single queued or in-flight comparison generation. State
/// machine guarded by the behavior methods below; illegal transitions throw
/// <see cref="InvalidOperationException"/>. Principle II compliance.
/// </summary>
public class ComparisonJob
{
    public Guid Id { get; private set; }
    public int ApplicationItemId { get; private set; }
    public string RequestedByUserId { get; private set; } = string.Empty;
    public ComparisonJobStatus Status { get; private set; }
    public bool BypassedRateLimit { get; private set; }
    public bool BypassedTokenCap { get; private set; }
    public DateTimeOffset LastStatusChangeAt { get; private set; }
    public int? ResultingArtifactId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private ComparisonJob() { }

    private ComparisonJob(
        Guid id,
        int applicationItemId,
        string requestedByUserId,
        bool bypassedRateLimit,
        bool bypassedTokenCap,
        DateTimeOffset now)
    {
        Id = id;
        ApplicationItemId = applicationItemId;
        RequestedByUserId = requestedByUserId;
        BypassedRateLimit = bypassedRateLimit;
        BypassedTokenCap = bypassedTokenCap;
        Status = ComparisonJobStatus.Pending;
        LastStatusChangeAt = now;
    }

    /// <summary>
    /// Static factory — sets initial Pending status + LastStatusChangeAt.
    /// Rejects empty user id / non-positive item id.
    /// </summary>
    public static ComparisonJob Enqueue(
        int applicationItemId,
        string requestedByUserId,
        bool bypassedRateLimit,
        bool bypassedTokenCap,
        DateTimeOffset now)
    {
        if (applicationItemId <= 0)
            throw new ArgumentException("ApplicationItemId must be positive.", nameof(applicationItemId));
        if (string.IsNullOrWhiteSpace(requestedByUserId))
            throw new ArgumentException("RequestedByUserId is required.", nameof(requestedByUserId));

        return new ComparisonJob(
            Guid.NewGuid(), applicationItemId, requestedByUserId,
            bypassedRateLimit, bypassedTokenCap, now);
    }

    /// <summary>Pending → Running. Rejects any other current state.</summary>
    public void Start(DateTimeOffset now)
    {
        if (Status != ComparisonJobStatus.Pending)
            throw new InvalidOperationException($"Cannot Start a job in status {Status}.");

        Status = ComparisonJobStatus.Running;
        StartedAt = now;
        LastStatusChangeAt = now;
    }

    /// <summary>Running → Completed. Rejects any other current state.</summary>
    public void RecordSuccess(int resultingArtifactId, DateTimeOffset now)
    {
        if (Status != ComparisonJobStatus.Running)
            throw new InvalidOperationException($"Cannot RecordSuccess from status {Status}.");
        if (resultingArtifactId <= 0)
            throw new ArgumentException("ResultingArtifactId must be positive.", nameof(resultingArtifactId));

        Status = ComparisonJobStatus.Completed;
        ResultingArtifactId = resultingArtifactId;
        FinishedAt = now;
        LastStatusChangeAt = now;
    }

    /// <summary>
    /// Pending|Running → Failed. Pre-flight guard rejects transition from
    /// Pending (sets only FinishedAt); mid-run failures transition from
    /// Running. Rejects any other current state.
    /// </summary>
    public void RecordFailure(string failureReason, DateTimeOffset now)
    {
        if (Status is not (ComparisonJobStatus.Pending or ComparisonJobStatus.Running))
            throw new InvalidOperationException($"Cannot RecordFailure from status {Status}.");
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("FailureReason is required.", nameof(failureReason));

        Status = ComparisonJobStatus.Failed;
        FailureReason = failureReason;
        FinishedAt = now;
        LastStatusChangeAt = now;
    }

    /// <summary>
    /// Edge case: worker crash. Transitions Running → Failed with
    /// failureReason=worker_crashed iff LastStatusChangeAt is older than the
    /// provided cutoff. Returns true when the reap fired.
    /// </summary>
    public bool Reap(DateTimeOffset cutoff, DateTimeOffset now)
    {
        if (Status != ComparisonJobStatus.Running) return false;
        if (LastStatusChangeAt >= cutoff) return false;

        Status = ComparisonJobStatus.Failed;
        FailureReason = "worker_crashed";
        FinishedAt = now;
        LastStatusChangeAt = now;
        return true;
    }
}
