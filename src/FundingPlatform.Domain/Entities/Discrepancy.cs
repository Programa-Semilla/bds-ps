using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 048 — a persisted, stateful reconciliation discrepancy. Turns the P1–P3 ephemeral,
/// computed-on-read hard blocks into a durable row with a fixed per-rule severity
/// (<see cref="DiscrepancySeverity.Blocking"/> / non-blocking <see cref="DiscrepancySeverity.Warning"/>)
/// and a lifecycle (<c>Open→Assigned→UnderCorrection→Resolved|Waived</c>) with per-discrepancy
/// correction history. A standalone Application-scoped aggregate (flat, no navigation on
/// <see cref="Application"/> — the P1/036 precedent) that copies the <see cref="Evidence"/> shape:
/// sealed, private setters, private EF ctor, static <see cref="Detect"/> factory, an owned
/// append-only <see cref="DiscrepancyEvent"/> chain, guarded transitions, and <see cref="RowVersion"/>.
///
/// <para>The engine (the materializer) OWNS detection/refresh/auto-resolve/auto-reopen; a user only
/// drives <see cref="Assign"/> / <see cref="MarkUnderCorrection"/> / <see cref="Waive"/>. There is no
/// manual resolve/reopen — resolution happens by fixing the numbers (FR-011). Persistence never
/// relaxes the money guarantee: the money gates keep recomputing fresh at the decision instant and
/// throwing; this row exists for visibility + lifecycle (persistence model C, SC-004).</para>
/// </summary>
public sealed class Discrepancy
{
    /// <summary>Zero-colón tolerance floor (NFR-001) — a difference at or below this is not detectable.</summary>
    public const decimal MinDetectableDifference = 0.01m;

    private readonly List<DiscrepancyEvent> _events = [];

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public DiscrepancyScopeType ScopeType { get; private set; }
    public int ScopeEntityId { get; private set; }
    public ReconciliationComparison Comparison { get; private set; }
    public DiscrepancySeverity Severity { get; private set; }
    public DiscrepancyState State { get; private set; }
    public decimal Expected { get; private set; }
    public decimal Actual { get; private set; }
    public decimal Difference { get; private set; }
    public decimal ToleranceApplied { get; private set; }
    public string SourceDocument { get; private set; } = string.Empty;
    public string? AssigneeUserId { get; private set; }
    public DateTimeOffset FirstDetectedAt { get; private set; }
    public DateTimeOffset LastEvaluatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? WaivedReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<DiscrepancyEvent> Events => _events.AsReadOnly();

    private Discrepancy() { } // EF

    /// <summary>
    /// FR-003 — detect a NEW discrepancy (state <see cref="DiscrepancyState.Open"/>). Called by the
    /// materializer when a computed discrepancy has no persisted row for its stable identity
    /// <c>(ApplicationId, ScopeType, ScopeEntityId, Comparison)</c>. Appends the genesis
    /// <c>Opened</c> event attributed to <paramref name="detectedByUserId"/> (the materializer passes
    /// the system-sentinel id — never the literal <c>"system"</c>, spec-043 lesson).
    /// </summary>
    public static Discrepancy Detect(
        int applicationId,
        DiscrepancyScopeType scopeType,
        int scopeEntityId,
        ReconciliationComparison comparison,
        DiscrepancySeverity severity,
        decimal expected,
        decimal actual,
        decimal toleranceApplied,
        string sourceDocument,
        string detectedByUserId,
        DateTimeOffset nowUtc)
    {
        if (applicationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(applicationId));
        }
        if (string.IsNullOrWhiteSpace(detectedByUserId))
        {
            throw new ArgumentException("DetectedByUserId is required.", nameof(detectedByUserId));
        }

        var discrepancy = new Discrepancy
        {
            ApplicationId = applicationId,
            ScopeType = scopeType,
            ScopeEntityId = scopeEntityId,
            Comparison = comparison,
            Severity = severity,
            State = DiscrepancyState.Open,
            Expected = expected,
            Actual = actual,
            Difference = actual - expected,
            ToleranceApplied = toleranceApplied,
            SourceDocument = (sourceDocument ?? string.Empty).Trim(),
            FirstDetectedAt = nowUtc,
            LastEvaluatedAt = nowUtc,
        };

        discrepancy._events.Add(new DiscrepancyEvent(
            DiscrepancyState.Open, DiscrepancyState.Open, DiscrepancyEvent.KindOpened, detectedByUserId, nowUtc));

        return discrepancy;
    }

    /// <summary>
    /// FR-003 — the materializer touches an already-persisted, still-failing row: refresh the amounts
    /// and <see cref="LastEvaluatedAt"/> while keeping state and assignee. A <see cref="DiscrepancyState.Waived"/>
    /// row whose amounts changed re-opens (FR-016) — a waiver accepts a specific number, not a moving one.
    /// (A <see cref="DiscrepancyState.Resolved"/> row that recurs is re-opened by the materializer via
    /// <see cref="AutoReopen"/> first.)
    /// </summary>
    public void Refresh(decimal expected, decimal actual, string systemUserId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(systemUserId))
        {
            throw new ArgumentException("SystemUserId is required.", nameof(systemUserId));
        }

        var amountChanged = expected != Expected || actual != Actual;

        Expected = expected;
        Actual = actual;
        Difference = actual - expected;
        LastEvaluatedAt = nowUtc;

        if (State == DiscrepancyState.Waived && amountChanged)
        {
            var from = State;
            State = DiscrepancyState.Open;
            ResolvedAt = null;
            WaivedReason = null;
            _events.Add(new DiscrepancyEvent(
                from, DiscrepancyState.Open, DiscrepancyEvent.KindReopened, systemUserId, nowUtc,
                note: "Reabierta: el monto cambió tras la exoneración."));
        }
    }

    /// <summary>FR-011 — a non-terminal discrepancy whose condition cleared: auto-resolve. Idempotent
    /// (no-op if already <see cref="DiscrepancyState.Resolved"/>/<see cref="DiscrepancyState.Waived"/>).</summary>
    public void AutoResolve(string systemUserId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(systemUserId))
        {
            throw new ArgumentException("SystemUserId is required.", nameof(systemUserId));
        }
        if (State is DiscrepancyState.Resolved or DiscrepancyState.Waived)
        {
            return;
        }

        var from = State;
        State = DiscrepancyState.Resolved;
        ResolvedAt = nowUtc;
        LastEvaluatedAt = nowUtc;
        _events.Add(new DiscrepancyEvent(
            from, DiscrepancyState.Resolved, DiscrepancyEvent.KindResolved, systemUserId, nowUtc));
    }

    /// <summary>FR-016 — a <see cref="DiscrepancyState.Resolved"/> discrepancy that recurs: auto-reopen.
    /// Idempotent (no-op if already non-terminal).</summary>
    public void AutoReopen(string systemUserId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(systemUserId))
        {
            throw new ArgumentException("SystemUserId is required.", nameof(systemUserId));
        }
        if (State is not (DiscrepancyState.Resolved or DiscrepancyState.Waived))
        {
            return;
        }

        var from = State;
        State = DiscrepancyState.Open;
        ResolvedAt = null;
        WaivedReason = null;
        LastEvaluatedAt = nowUtc;
        _events.Add(new DiscrepancyEvent(
            from, DiscrepancyState.Open, DiscrepancyEvent.KindReopened, systemUserId, nowUtc));
    }

    /// <summary>True when the discrepancy is in a terminal rung (<see cref="DiscrepancyState.Resolved"/>
    /// or <see cref="DiscrepancyState.Waived"/>) — the engine owns leaving these (auto-reopen), so a user
    /// may not assign / mark-under-correction them.</summary>
    public bool IsTerminal => State is DiscrepancyState.Resolved or DiscrepancyState.Waived;

    /// <summary>FR-007 — assign the discrepancy to a responsible operator. Refused once the discrepancy
    /// is Resolved or Waived (re-activation is the engine's job, never a manual state change).</summary>
    public void Assign(string assigneeUserId, string actorUserId, DateTimeOffset nowUtc)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"A {State} discrepancy cannot be assigned.");
        }
        if (string.IsNullOrWhiteSpace(assigneeUserId))
        {
            throw new ArgumentException("AssigneeUserId is required.", nameof(assigneeUserId));
        }
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        var from = State;
        AssigneeUserId = assigneeUserId;
        State = DiscrepancyState.Assigned;
        _events.Add(new DiscrepancyEvent(
            from, DiscrepancyState.Assigned, DiscrepancyEvent.KindAssigned, actorUserId, nowUtc,
            note: $"Asignada a {assigneeUserId}"));
    }

    /// <summary>FR-007 — mark the discrepancy as actively being corrected. Refused once the discrepancy
    /// is Resolved or Waived.</summary>
    public void MarkUnderCorrection(string actorUserId, string? note, DateTimeOffset nowUtc)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"A {State} discrepancy cannot be marked under correction.");
        }
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        var from = State;
        State = DiscrepancyState.UnderCorrection;
        _events.Add(new DiscrepancyEvent(
            from, DiscrepancyState.UnderCorrection, DiscrepancyEvent.KindUnderCorrection, actorUserId, nowUtc,
            note: note));
    }

    /// <summary>FR-008 — deliberately accept a non-blocking Warning (reason required). Throws when the
    /// discrepancy is <see cref="DiscrepancySeverity.Blocking"/> (a Blocking discrepancy can never be
    /// waived — it must be corrected) or when the reason is blank.</summary>
    /// <exception cref="InvalidOperationException">The discrepancy is Blocking.</exception>
    /// <exception cref="ArgumentException">The reason is blank.</exception>
    public void Waive(string reason, string actorUserId, DateTimeOffset nowUtc)
    {
        if (Severity == DiscrepancySeverity.Blocking)
        {
            throw new InvalidOperationException("A Blocking discrepancy cannot be waived; it must be corrected.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to waive a discrepancy.", nameof(reason));
        }
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        var from = State;
        State = DiscrepancyState.Waived;
        WaivedReason = reason.Trim();
        ResolvedAt = null;
        _events.Add(new DiscrepancyEvent(
            from, DiscrepancyState.Waived, DiscrepancyEvent.KindWaived, actorUserId, nowUtc, reason: reason.Trim()));
    }
}
