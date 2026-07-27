using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Services;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 048 / FR-010 — the pure Warning-tier evaluator. Each of the three rules: positive, negative,
/// and boundary at the 0.01 detectable floor; the date-anomaly rule in both directions.
/// </summary>
[TestFixture]
public class ReconciliationWarningsTests
{
    private static readonly DateOnly Execution = new(2026, 6, 1);
    private static readonly DateOnly Paid = new(2026, 6, 15);

    // ---------------------------------------------------------------- (a) evidence date anomaly

    [Test]
    public void DateAnomaly_DocAfterPayment_Flags()
    {
        var result = ReconciliationWarnings.EvaluateEvidenceDateAnomalies(
            [new EvidenceDateInput(EvidenceId: 7, Amount: 100m, DocumentDate: new(2026, 6, 16), PaymentDate: Paid)],
            Execution);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Comparison, Is.EqualTo(ReconciliationComparison.EvidenceDateAnomaly));
            Assert.That(result[0].ScopeType, Is.EqualTo(DiscrepancyScopeType.Document));
            Assert.That(result[0].ScopeEntityId, Is.EqualTo(7));
            Assert.That(result[0].SourceDocument, Is.EqualTo(ReconciliationWarnings.SourceDateAfterPayment));
        });
    }

    [Test]
    public void DateAnomaly_DocBeforeExecution_Flags()
    {
        var result = ReconciliationWarnings.EvaluateEvidenceDateAnomalies(
            [new EvidenceDateInput(9, 100m, DocumentDate: new(2026, 5, 31), PaymentDate: null)],
            Execution);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].SourceDocument, Is.EqualTo(ReconciliationWarnings.SourceDateBeforeExecution));
    }

    [Test]
    public void DateAnomaly_OnPaymentDate_IsClean()
    {
        // Boundary: a document dated exactly on the payment date (and on/after execution) is clean.
        var result = ReconciliationWarnings.EvaluateEvidenceDateAnomalies(
            [new EvidenceDateInput(1, 100m, DocumentDate: Paid, PaymentDate: Paid)],
            Execution);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void DateAnomaly_OnExecutionDate_IsClean()
    {
        var result = ReconciliationWarnings.EvaluateEvidenceDateAnomalies(
            [new EvidenceDateInput(1, 100m, DocumentDate: Execution, PaymentDate: null)],
            Execution);

        Assert.That(result, Is.Empty);
    }

    // ---------------------------------------------------------------- (b) duplicate payment

    [Test]
    public void Duplicate_SameSupplierAmountDate_FlagsBoth()
    {
        var result = ReconciliationWarnings.EvaluatePossibleDuplicatePayments(
        [
            new PaymentFingerprint(DisbursementId: 1, SupplierId: 5, Amount: 500_000m, PaymentDate: Paid),
            new PaymentFingerprint(DisbursementId: 2, SupplierId: 5, Amount: 500_000m, PaymentDate: Paid),
        ]);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(w => w.ScopeEntityId), Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(result.All(w => w.Comparison == ReconciliationComparison.PossibleDuplicatePayment));
        Assert.That(result.All(w => w.Expected == w.Actual)); // duplication, not amount mismatch → difference 0
    }

    [Test]
    public void Duplicate_DifferentSupplier_IsClean()
    {
        var result = ReconciliationWarnings.EvaluatePossibleDuplicatePayments(
        [
            new PaymentFingerprint(1, SupplierId: 5, 500_000m, Paid),
            new PaymentFingerprint(2, SupplierId: 6, 500_000m, Paid),
        ]);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Duplicate_DifferentDate_IsClean()
    {
        var result = ReconciliationWarnings.EvaluatePossibleDuplicatePayments(
        [
            new PaymentFingerprint(1, 5, 500_000m, Paid),
            new PaymentFingerprint(2, 5, 500_000m, PaymentDate: new(2026, 6, 16)),
        ]);

        Assert.That(result, Is.Empty);
    }

    // ---------------------------------------------------------------- (c) graph-invoice drift

    [Test]
    public void GraphInvoiceDrift_PaidExceedsAllocated_Flags()
    {
        var result = ReconciliationWarnings.EvaluateGraphInvoiceAllocationDrift(
            [new LineInvoiceDriftInput(ItemId: 3, LineLabel: "L-3", ValidatedPaid: 100_000m, GraphInvoiceAllocated: 90_000m)]);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Comparison, Is.EqualTo(ReconciliationComparison.GraphInvoiceAllocationDrift));
            Assert.That(result[0].ScopeType, Is.EqualTo(DiscrepancyScopeType.BudgetLine));
            Assert.That(result[0].Expected, Is.EqualTo(90_000m));
            Assert.That(result[0].Actual, Is.EqualTo(100_000m));
        });
    }

    [Test]
    public void GraphInvoiceDrift_ExactMatch_IsClean()
    {
        var result = ReconciliationWarnings.EvaluateGraphInvoiceAllocationDrift(
            [new LineInvoiceDriftInput(3, "L-3", ValidatedPaid: 100_000m, GraphInvoiceAllocated: 100_000m)]);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GraphInvoiceDrift_BoundaryAtOneCentimo_Flags()
    {
        var result = ReconciliationWarnings.EvaluateGraphInvoiceAllocationDrift(
            [new LineInvoiceDriftInput(3, "L-3", ValidatedPaid: 100_000.01m, GraphInvoiceAllocated: 100_000m)]);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void GraphInvoiceDrift_BelowFloor_IsClean()
    {
        var result = ReconciliationWarnings.EvaluateGraphInvoiceAllocationDrift(
            [new LineInvoiceDriftInput(3, "L-3", ValidatedPaid: 100_000.009m, GraphInvoiceAllocated: 100_000m)]);

        Assert.That(result, Is.Empty);
    }
}
