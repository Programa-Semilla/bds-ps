using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Services;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 045 / FR-011/FR-012/FR-015 — the pure, deterministic reconciliation evaluator.
/// Zero-colón tolerance; a missing evidence document is incompleteness, not a discrepancy;
/// over-disbursement is flagged, under-disbursement is not.
/// </summary>
[TestFixture]
public class DisbursementReconciliationEvaluatorTests
{
    // Allocation comfortably above the disbursement so comparison (c) stays clean unless asserted.
    private const decimal BigAllocation = 10_000_000m;

    [Test]
    public void ExactMatch_AllThree_Clean()
    {
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 85_800m,
            bankReceiptAmount: 85_800m,
            invoiceAmount: 85_800m,
            sumOfNonCancelledIncludingThis: 85_800m,
            allocation: BigAllocation);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void InvoiceOffByOneColon_FlagsInvoiceDiscrepancy_ReferenceCase()
    {
        // AC-001 / SC-001: pago ₡85,800 vs factura ₡85,728 → ₡72.
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 85_800m,
            bankReceiptAmount: 85_800m,
            invoiceAmount: 85_728m,
            sumOfNonCancelledIncludingThis: 85_800m,
            allocation: BigAllocation);

        Assert.That(result, Has.Count.EqualTo(1));
        var d = result[0];
        Assert.That(d.Comparison, Is.EqualTo(ReconciliationComparison.DisbursementVsInvoice));
        Assert.That(d.SourceDocument, Is.EqualTo(DisbursementReconciliation.SourceInvoice));
        Assert.That(Math.Abs(d.Difference), Is.EqualTo(72m));
        Assert.That(d.Severity, Is.EqualTo(DiscrepancySeverity.Blocking));
    }

    [Test]
    public void OneColonBoundary_IsFlagged()
    {
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 100m,
            bankReceiptAmount: 100.01m,
            invoiceAmount: 100m,
            sumOfNonCancelledIncludingThis: 100m,
            allocation: BigAllocation);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Comparison, Is.EqualTo(ReconciliationComparison.DisbursementVsBankReceipt));
    }

    [Test]
    public void JustUnderOneColon_IsNotFlagged()
    {
        // 0.009 rounds below the 1-colón (0.01) threshold — no discrepancy (zero tolerance
        // detects down to exactly one colón, not fractions of one).
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 100m,
            bankReceiptAmount: 100.009m,
            invoiceAmount: 100m,
            sumOfNonCancelledIncludingThis: 100m,
            allocation: BigAllocation);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void MissingInvoice_IsNotADiscrepancy()
    {
        // Only the bank receipt is present; the invoice comparison simply does not run.
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 500m,
            bankReceiptAmount: 500m,
            invoiceAmount: null,
            sumOfNonCancelledIncludingThis: 500m,
            allocation: BigAllocation);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BothEvidencesDiffer_BothComparisonsReported()
    {
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 1_000m,
            bankReceiptAmount: 1_010m,
            invoiceAmount: 990m,
            sumOfNonCancelledIncludingThis: 1_000m,
            allocation: BigAllocation);

        Assert.That(result.Select(d => d.Comparison), Is.EquivalentTo(new[]
        {
            ReconciliationComparison.DisbursementVsBankReceipt,
            ReconciliationComparison.DisbursementVsInvoice,
        }));
    }

    [Test]
    public void OverDisbursement_FlagsTotalVsAllocation_WithPositiveDifference()
    {
        // Σ = 1,100,000 against a 1,000,000 allocation → 100,000 over.
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 500_000m,
            bankReceiptAmount: 500_000m,
            invoiceAmount: 500_000m,
            sumOfNonCancelledIncludingThis: 1_100_000m,
            allocation: 1_000_000m);

        Assert.That(result, Has.Count.EqualTo(1));
        var d = result[0];
        Assert.That(d.Comparison, Is.EqualTo(ReconciliationComparison.TotalVsAllocation));
        Assert.That(d.SourceDocument, Is.EqualTo(DisbursementReconciliation.SourceDisbursementSet));
        Assert.That(d.Difference, Is.EqualTo(100_000m));
    }

    [Test]
    public void SumExactlyAtAllocation_IsNotADiscrepancy()
    {
        // Boundary: allocation fully consumed, Available → 0, no over-disbursement.
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 400_000m,
            bankReceiptAmount: 400_000m,
            invoiceAmount: 400_000m,
            sumOfNonCancelledIncludingThis: 1_000_000m,
            allocation: 1_000_000m);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void UnderDisbursement_IsNeverADiscrepancy()
    {
        var result = DisbursementReconciliation.Evaluate(
            disbursementAmount: 400_000m,
            bankReceiptAmount: 400_000m,
            invoiceAmount: 400_000m,
            sumOfNonCancelledIncludingThis: 400_000m,
            allocation: 1_000_000m);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Deterministic_SameInputsSameResult()
    {
        var a = DisbursementReconciliation.Evaluate(1_000m, 1_010m, 990m, 5_000m, 4_000m);
        var b = DisbursementReconciliation.Evaluate(1_000m, 1_010m, 990m, 5_000m, 4_000m);
        Assert.That(a, Is.EqualTo(b));
    }
}
