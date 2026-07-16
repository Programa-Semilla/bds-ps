using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Services;

/// <summary>
/// Spec 045 / FR-011/FR-012 — the pure, deterministic reconciliation evaluator
/// (NFR-020). Runs the three comparisons at a <b>zero-colón tolerance</b>: any
/// non-zero difference (down to a single céntimo, <c>|a − b| ≥ 0.01</c>) yields a
/// blocking discrepancy (FR-015) — so a one-colón difference is always caught.
/// Discrepancies are computed, never persisted (research R4); only the derived
/// <see cref="DisbursementState"/> is stored, so re-running the evaluator on read never drifts.
/// </summary>
public static class DisbursementReconciliation
{
    /// <summary>Zero tolerance — the smallest detectable difference is one céntimo (0.01),
    /// so any difference of one colón or more is always flagged.</summary>
    private const decimal MinDetectableDifference = 0.01m;

    // es-CR source-document labels (FR-013/FR-014 — status is by text, not colour alone).
    public const string SourceBankReceipt = "comprobante bancario";
    public const string SourceInvoice = "factura";
    public const string SourceDisbursementSet = "conjunto de desembolsos";

    /// <param name="disbursementAmount">The amount the operator recorded.</param>
    /// <param name="bankReceiptAmount">The bank receipt's reconciled amount, or null if not yet attached.</param>
    /// <param name="invoiceAmount">The invoice's reconciled amount, or null if not yet attached.</param>
    /// <param name="sumOfNonCancelledIncludingThis">Σ of all non-cancelled disbursements for the
    /// application, including this one at its current amount (comparison c).</param>
    /// <param name="allocation">The executed agreement total (the allocation ceiling).</param>
    /// <returns>An ordered list of blocking discrepancies. Empty ⇒ clean. A missing evidence
    /// document is incompleteness, not a discrepancy (its comparison simply does not run).</returns>
    public static IReadOnlyList<ReconciliationDiscrepancy> Evaluate(
        decimal disbursementAmount,
        decimal? bankReceiptAmount,
        decimal? invoiceAmount,
        decimal sumOfNonCancelledIncludingThis,
        decimal allocation)
    {
        var discrepancies = new List<ReconciliationDiscrepancy>(3);

        // (a) Disbursement vs bank receipt — only when the receipt exists.
        if (bankReceiptAmount is { } receipt && Math.Abs(receipt - disbursementAmount) >= MinDetectableDifference)
        {
            discrepancies.Add(new ReconciliationDiscrepancy(
                ReconciliationComparison.DisbursementVsBankReceipt,
                Expected: disbursementAmount,
                Actual: receipt,
                Difference: receipt - disbursementAmount,
                SourceDocument: SourceBankReceipt,
                Severity: DiscrepancySeverity.Blocking));
        }

        // (b) Disbursement vs invoice — only when the invoice exists.
        if (invoiceAmount is { } invoice && Math.Abs(invoice - disbursementAmount) >= MinDetectableDifference)
        {
            discrepancies.Add(new ReconciliationDiscrepancy(
                ReconciliationComparison.DisbursementVsInvoice,
                Expected: disbursementAmount,
                Actual: invoice,
                Difference: invoice - disbursementAmount,
                SourceDocument: SourceInvoice,
                Severity: DiscrepancySeverity.Blocking));
        }

        // (c) Sum of disbursements vs allocation — always. Over-disbursement only
        // (under-disbursement is never a discrepancy, FR-005).
        if (sumOfNonCancelledIncludingThis - allocation >= MinDetectableDifference)
        {
            discrepancies.Add(new ReconciliationDiscrepancy(
                ReconciliationComparison.TotalVsAllocation,
                Expected: allocation,
                Actual: sumOfNonCancelledIncludingThis,
                Difference: sumOfNonCancelledIncludingThis - allocation,
                SourceDocument: SourceDisbursementSet,
                Severity: DiscrepancySeverity.Blocking));
        }

        return discrepancies;
    }
}
