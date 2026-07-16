using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 046 / FR-019 — a per-line over-payment: the Σ of payments attributed to a budget-line
/// exceeds its committed budget. Produced by the pure
/// <see cref="Services.DisbursementLineReconciliation.EvaluateLineOverpayments"/> evaluator and
/// re-checked at Validar against freshly-read sums (symmetric with P1's participant-level
/// over-disbursement gate). Line-scoped sibling of <see cref="ReconciliationDiscrepancy"/>; every
/// instance is <see cref="DiscrepancySeverity.Blocking"/>.
/// </summary>
/// <param name="ItemId">The budget-line whose payments over-run.</param>
/// <param name="LineLabel">Line label for the message: the reviewer-assigned <c>LineCode</c>, or an <c>L-{itemId}</c> fallback.</param>
/// <param name="Committed">The line's committed budget (the ceiling).</param>
/// <param name="Paid">Σ non-cancelled payments attributed to the line.</param>
/// <param name="Overage">Signed <c>Paid − Committed</c> (kept for display).</param>
/// <param name="Severity">Blocking in P2.</param>
public sealed record LineOverpaymentDiscrepancy(
    int ItemId,
    string LineLabel,
    decimal Committed,
    decimal Paid,
    decimal Overage,
    DiscrepancySeverity Severity);

/// <summary>
/// Spec 046 / FR-019 — one input row to
/// <see cref="Services.DisbursementLineReconciliation.EvaluateLineOverpayments"/>: a budget-line's
/// committed budget paired with the Σ of non-cancelled payments attributed to it.
/// </summary>
public readonly record struct LinePaymentVsBudget(
    int ItemId,
    string LineLabel,
    decimal CommittedBudget,
    decimal PaidToLine);
