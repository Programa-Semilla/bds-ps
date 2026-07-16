namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 045 / FR-019 — the participant's five-dimension financial position, projected
/// from the append-only ledger plus mutable pending disbursements (research R3). NOT
/// stored (FR-017 — no denormalized balance column). Every figure traces to underlying
/// transactions (FR-023).
///
/// Definitions:
/// <list type="bullet">
///   <item><c>Allocated</c> = executed agreement total (the allocation ceiling).</item>
///   <item><c>Validated</c> = Σ validated disbursements (ledger Disbursement entries).</item>
///   <item><c>PendingValidation</c> = Σ pending disbursements (state Recorded/Inconsistent).</item>
///   <item><c>Paid</c> = <c>Validated + PendingValidation</c> (money that left the bank).</item>
///   <item><c>Available</c> = <c>Allocated − Paid</c> — MAY be negative (over-disbursement, FR-020); never clamped.</item>
/// </list>
/// </summary>
public sealed record ParticipantBalance(
    decimal Allocated,
    decimal Paid,
    decimal Validated,
    decimal PendingValidation,
    decimal Available);
