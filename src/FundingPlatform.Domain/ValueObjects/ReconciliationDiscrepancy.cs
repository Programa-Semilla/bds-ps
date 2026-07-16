using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 045 / FR-013 — a detected mismatch between two amounts, produced by the pure
/// <see cref="Services.DisbursementReconciliation"/> evaluator. Computed on read and
/// rendered on the disbursement surface; NOT persisted in P1 (only the derived
/// <see cref="DisbursementState"/> is stored — research R4). Every discrepancy in this
/// slice is <see cref="DiscrepancySeverity.Blocking"/> (FR-015).
/// </summary>
/// <param name="Comparison">Which of the three comparisons produced this discrepancy.</param>
/// <param name="Expected">The reference amount (the disbursement amount, or the allocation).</param>
/// <param name="Actual">The observed amount (the evidence amount, or the sum of disbursements).</param>
/// <param name="Difference">Signed difference <c>Actual − Expected</c> (kept for display).</param>
/// <param name="SourceDocument">es-CR label naming the source of the difference (e.g. "factura").</param>
/// <param name="Severity">Blocking in P1.</param>
public sealed record ReconciliationDiscrepancy(
    ReconciliationComparison Comparison,
    decimal Expected,
    decimal Actual,
    decimal Difference,
    string SourceDocument,
    DiscrepancySeverity Severity);
