using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Services;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 046 / FR-013/FR-019 — the pure line-level reconciliation evaluator: split integrity and
/// per-line over-payment, both at a zero-colón (0.01) tolerance, all discrepancies Blocking.
/// </summary>
[TestFixture]
public class DisbursementLineReconciliationTests
{
    // ---------- Split integrity ----------

    [Test]
    public void EvaluateSplit_ExactMatch_NoDiscrepancy()
    {
        var lines = new List<(int, decimal)> { (1, 60_000m), (2, 40_000m) };
        var result = DisbursementLineReconciliation.EvaluateSplit(100_000m, lines);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EvaluateSplit_OneColonShort_Blocks()
    {
        var lines = new List<(int, decimal)> { (1, 60_000m), (2, 39_999m) };
        var result = DisbursementLineReconciliation.EvaluateSplit(100_000m, lines);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Comparison, Is.EqualTo(ReconciliationComparison.DisbursementSplitVsTotal));
        Assert.That(result[0].Severity, Is.EqualTo(DiscrepancySeverity.Blocking));
        Assert.That(result[0].Difference, Is.EqualTo(-1m));
    }

    [Test]
    public void EvaluateSplit_OverByOneCentimo_Blocks()
    {
        var lines = new List<(int, decimal)> { (1, 100_000.01m) };
        var result = DisbursementLineReconciliation.EvaluateSplit(100_000m, lines);
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void EvaluateSplit_EmptyWithPositiveAmount_Blocks()
    {
        var result = DisbursementLineReconciliation.EvaluateSplit(100_000m, []);
        Assert.That(result, Has.Count.EqualTo(1)); // Σ 0 ≠ 100k
    }

    // ---------- Per-line over-payment ----------

    [Test]
    public void EvaluateLineOverpayments_WithinBudget_NoDiscrepancy()
    {
        var lines = new List<LinePaymentVsBudget>
        {
            new(1, "L-1", CommittedBudget: 100_000m, PaidToLine: 100_000m),
            new(2, "L-2", CommittedBudget: 50_000m, PaidToLine: 20_000m),
        };
        var result = DisbursementLineReconciliation.EvaluateLineOverpayments(lines);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EvaluateLineOverpayments_OverByOneColon_BlocksThatLineOnly()
    {
        var lines = new List<LinePaymentVsBudget>
        {
            new(1, "L-1", CommittedBudget: 100_000m, PaidToLine: 100_001m),
            new(2, "L-2", CommittedBudget: 50_000m, PaidToLine: 50_000m),
        };
        var result = DisbursementLineReconciliation.EvaluateLineOverpayments(lines);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ItemId, Is.EqualTo(1));
        Assert.That(result[0].LineLabel, Is.EqualTo("L-1"));
        Assert.That(result[0].Overage, Is.EqualTo(1m));
        Assert.That(result[0].Severity, Is.EqualTo(DiscrepancySeverity.Blocking));
    }

    [Test]
    public void EvaluateLineOverpayments_UnderPayment_NeverBlocks()
    {
        var lines = new List<LinePaymentVsBudget> { new(1, "L-1", 100_000m, 1m) };
        var result = DisbursementLineReconciliation.EvaluateLineOverpayments(lines);
        Assert.That(result, Is.Empty);
    }
}
