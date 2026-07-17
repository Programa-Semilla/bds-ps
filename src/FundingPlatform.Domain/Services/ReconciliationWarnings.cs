using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Services;

/// <summary>
/// Spec 048 / FR-010 — the pure, deterministic <b>Warning</b>-tier evaluator (NFR-020), sibling to
/// P1's blocking <see cref="DisbursementReconciliation"/> and P2/P3's
/// <see cref="DisbursementLineReconciliation"/>. Detects the three non-blocking conditions of the P4
/// starter set: (a) evidence date anomalies, (b) possible duplicate payments, (c) graph-invoice
/// allocation drift. Every output is a <see cref="WarningDescriptor"/> the materializer maps onto a
/// persisted <see cref="Entities.Discrepancy"/> row with fixed severity
/// <see cref="DiscrepancySeverity.Warning"/>. Inputs carry only primitives (ids, decimals,
/// <see cref="DateOnly"/>) so the evaluator stays pure. es-CR source labels follow the P1 precedent
/// (status by text, not colour alone — FR-025).
/// </summary>
public static class ReconciliationWarnings
{
    /// <summary>Zero tolerance — the smallest detectable difference is one céntimo (0.01).</summary>
    public const decimal MinDetectableDifference = 0.01m;

    public const string SourceDateAfterPayment = "documento con fecha posterior al pago";
    public const string SourceDateBeforeExecution = "documento con fecha anterior a la ejecución del convenio";
    public const string SourceDuplicatePayment = "posible pago duplicado (mismo proveedor, monto y fecha)";
    public const string SourceGraphInvoiceDrift = "diferencia con la factura del grafo de evidencia";

    /// <summary>
    /// FR-010(a) — an evidence document dated <b>after its related payment date</b>, or dated
    /// <b>before the funding-agreement execution date</b>. One warning per document (the after-payment
    /// condition takes precedence in the label when both hold). A document with no linked payment is
    /// only checked against the execution date.
    /// </summary>
    public static IReadOnlyList<WarningDescriptor> EvaluateEvidenceDateAnomalies(
        IReadOnlyList<EvidenceDateInput> evidence, DateOnly agreementExecutionDate)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var warnings = new List<WarningDescriptor>();
        foreach (var e in evidence)
        {
            string? source = null;
            if (e.PaymentDate is { } paidOn && e.DocumentDate > paidOn)
            {
                source = SourceDateAfterPayment;
            }
            else if (e.DocumentDate < agreementExecutionDate)
            {
                source = SourceDateBeforeExecution;
            }

            if (source is not null)
            {
                warnings.Add(new WarningDescriptor(
                    DiscrepancyScopeType.Document, e.EvidenceId, ReconciliationComparison.EvidenceDateAnomaly,
                    Expected: e.Amount, Actual: e.Amount, SourceDocument: source));
            }
        }
        return warnings;
    }

    /// <summary>
    /// FR-010(b) — possible duplicate payment: the same supplier + amount + payment date across more
    /// than one non-cancelled disbursement. Emits one warning per participating disbursement (each can
    /// be independently waived). Fingerprints carry a known supplier only — a payment whose supplier
    /// cannot be resolved is not a duplicate signal. Amount matches exactly, so <c>Difference = 0</c>:
    /// the warning is about duplication, not an amount mismatch.
    /// </summary>
    public static IReadOnlyList<WarningDescriptor> EvaluatePossibleDuplicatePayments(
        IReadOnlyList<PaymentFingerprint> payments)
    {
        ArgumentNullException.ThrowIfNull(payments);

        var warnings = new List<WarningDescriptor>();
        var groups = payments.GroupBy(p => (p.SupplierId, p.Amount, p.PaymentDate));
        foreach (var group in groups)
        {
            if (group.Count() < 2)
            {
                continue;
            }
            foreach (var p in group)
            {
                warnings.Add(new WarningDescriptor(
                    DiscrepancyScopeType.Payment, p.DisbursementId, ReconciliationComparison.PossibleDuplicatePayment,
                    Expected: p.Amount, Actual: p.Amount, SourceDocument: SourceDuplicatePayment));
            }
        }
        return warnings;
    }

    /// <summary>
    /// FR-010(c) — graph-invoice allocation drift (047 FINDING-13): a budget-line with a validated
    /// payment whose Σ validated payments differs from the Σ of the line's independently-allocated
    /// graph invoices by more than the tolerance (either direction). The caller passes only lines that
    /// have a validated payment. <c>Expected</c> = graph-invoice allocation; <c>Actual</c> = validated paid.
    /// </summary>
    public static IReadOnlyList<WarningDescriptor> EvaluateGraphInvoiceAllocationDrift(
        IReadOnlyList<LineInvoiceDriftInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var warnings = new List<WarningDescriptor>();
        foreach (var line in lines)
        {
            if (Math.Abs(line.ValidatedPaid - line.GraphInvoiceAllocated) >= MinDetectableDifference)
            {
                warnings.Add(new WarningDescriptor(
                    DiscrepancyScopeType.BudgetLine, line.ItemId, ReconciliationComparison.GraphInvoiceAllocationDrift,
                    Expected: line.GraphInvoiceAllocated, Actual: line.ValidatedPaid, SourceDocument: SourceGraphInvoiceDrift));
            }
        }
        return warnings;
    }
}

/// <summary>Spec 048 — one evidence document for the date-anomaly rule: its amount, its document date,
/// and its related payment date (null when the document is not anchored to a disbursement).</summary>
public readonly record struct EvidenceDateInput(int EvidenceId, decimal Amount, DateOnly DocumentDate, DateOnly? PaymentDate);

/// <summary>Spec 048 — one non-cancelled disbursement's duplicate-detection fingerprint. <c>SupplierId</c>
/// is a resolved, known supplier (the materializer omits payments with no resolvable supplier).</summary>
public readonly record struct PaymentFingerprint(int DisbursementId, int SupplierId, decimal Amount, DateOnly PaymentDate);

/// <summary>Spec 048 — one budget-line (with a validated payment) for the graph-invoice-drift rule:
/// its Σ validated payments paired with its Σ independently-allocated graph invoices.</summary>
public readonly record struct LineInvoiceDriftInput(int ItemId, string LineLabel, decimal ValidatedPaid, decimal GraphInvoiceAllocated);
