using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Services;

/// <summary>
/// Spec 046 / FR-013/FR-019 — the pure, deterministic line-level reconciliation evaluator (research
/// D6), sibling to P1's <see cref="DisbursementReconciliation"/>. Two zero-colón checks at the same
/// <c>MinDetectableDifference = 0.01</c> tolerance, both producing <see cref="DiscrepancySeverity.Blocking"/>
/// discrepancies:
/// <list type="bullet">
///   <item><b>Split integrity</b> (at Record/Edit): Σ a disbursement's per-line allocations must equal
///     the disbursement amount.</item>
///   <item><b>Per-line over-payment</b> (at Validar): Σ non-cancelled payments attributed to a line must
///     not exceed its committed budget — re-checked against freshly-read sums (the P1 R5 race lesson).</item>
/// </list>
/// </summary>
public static class DisbursementLineReconciliation
{
    /// <summary>Zero tolerance — the smallest detectable difference is one céntimo (0.01).</summary>
    public const decimal MinDetectableDifference = 0.01m;

    /// <summary>es-CR source label for the split-integrity discrepancy.</summary>
    public const string SourceLineSplit = "distribución por línea";

    /// <summary>
    /// Split integrity: a single blocking discrepancy iff <c>|Σ line amounts − disbursementAmount| ≥ 0.01</c>.
    /// An empty split with a positive amount is a mismatch (Σ = 0 ≠ amount).
    /// </summary>
    public static IReadOnlyList<ReconciliationDiscrepancy> EvaluateSplit(
        decimal disbursementAmount, IReadOnlyList<(int ItemId, decimal Amount)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var sum = lines.Sum(l => l.Amount);
        if (Math.Abs(sum - disbursementAmount) < MinDetectableDifference)
        {
            return [];
        }

        return
        [
            new ReconciliationDiscrepancy(
                ReconciliationComparison.DisbursementSplitVsTotal,
                Expected: disbursementAmount,
                Actual: sum,
                Difference: sum - disbursementAmount,
                SourceDocument: SourceLineSplit,
                Severity: DiscrepancySeverity.Blocking),
        ];
    }

    /// <summary>
    /// Per-line over-payment: one blocking discrepancy per line where <c>PaidToLine − CommittedBudget ≥ 0.01</c>.
    /// Under-payment is never a discrepancy (a line may be partially paid). Symmetric with P1's
    /// participant-level over-disbursement check.
    /// </summary>
    public static IReadOnlyList<LineOverpaymentDiscrepancy> EvaluateLineOverpayments(
        IReadOnlyList<LinePaymentVsBudget> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var discrepancies = new List<LineOverpaymentDiscrepancy>();
        foreach (var line in lines)
        {
            var overage = line.PaidToLine - line.CommittedBudget;
            if (overage >= MinDetectableDifference)
            {
                discrepancies.Add(new LineOverpaymentDiscrepancy(
                    line.ItemId, line.LineLabel, line.CommittedBudget, line.PaidToLine, overage,
                    DiscrepancySeverity.Blocking));
            }
        }
        return discrepancies;
    }
}
