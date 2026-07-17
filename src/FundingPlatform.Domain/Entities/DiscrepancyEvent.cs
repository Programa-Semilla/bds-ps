using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 048 — an immutable, append-only history row on a <see cref="Discrepancy"/>: one entry per
/// lifecycle transition (detected, assigned, under-correction, resolved, waived, reopened). Copies
/// the <see cref="DisbursementLedgerEntry"/> shape — <b>no instance mutators</b>: append-only is
/// enforced by construction (only created through the <see cref="Discrepancy"/> root via the
/// <c>internal</c> ctor) and by service discipline (never updated or deleted; CASCADE-deleted with
/// its parent). <see cref="ActorUserId"/> is the real system-sentinel id for auto transitions
/// (spec-043 lesson — never the literal <c>"system"</c>, which violates the AspNetUsers FK).
/// </summary>
public sealed class DiscrepancyEvent
{
    /// <summary>Timeline labels (also the <c>Kind</c> column values).</summary>
    public const string KindOpened = "Opened";
    public const string KindAssigned = "Assigned";
    public const string KindUnderCorrection = "UnderCorrection";
    public const string KindResolved = "Resolved";
    public const string KindWaived = "Waived";
    public const string KindReopened = "Reopened";

    public int Id { get; private set; }
    public int DiscrepancyId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public DiscrepancyState FromState { get; private set; }
    public DiscrepancyState ToState { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? Note { get; private set; }

    private DiscrepancyEvent() { } // EF

    /// <summary>Created only by the <see cref="Discrepancy"/> aggregate root as it transitions.</summary>
    internal DiscrepancyEvent(
        DiscrepancyState fromState,
        DiscrepancyState toState,
        string kind,
        string actorUserId,
        DateTimeOffset occurredAt,
        string? reason = null,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Kind is required.", nameof(kind));
        }

        FromState = fromState;
        ToState = toState;
        Kind = kind;
        ActorUserId = actorUserId;
        OccurredAt = occurredAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
