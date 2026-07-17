namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-011 — identifies which reconciliation comparison produced a
/// <see cref="ValueObjects.ReconciliationDiscrepancy"/>. Spec 046 adds the two
/// line-level comparisons (research D6). Spec 048 adds the three Warning-tier comparisons
/// (5–7); the spec-048 materializer maps 0–4 → <see cref="DiscrepancySeverity.Blocking"/> and
/// 5–7 → <see cref="DiscrepancySeverity.Warning"/>.
/// </summary>
public enum ReconciliationComparison : byte
{
    /// <summary>(a) Disbursement amount vs bank receipt amount.</summary>
    DisbursementVsBankReceipt = 0,

    /// <summary>(b) Disbursement amount vs invoice amount.</summary>
    DisbursementVsInvoice = 1,

    /// <summary>(c) Sum of non-cancelled disbursements vs the executed agreement total.</summary>
    TotalVsAllocation = 2,

    /// <summary>Spec 046 / FR-013 — (d) Σ of a disbursement's per-line allocations vs the
    /// disbursement amount (split integrity, checked at Record/Edit).</summary>
    DisbursementSplitVsTotal = 3,

    /// <summary>Spec 046 / FR-019 — (e) Σ payments attributed to a budget-line vs its committed
    /// budget (per-line over-payment, re-checked at Validar).</summary>
    LinePaymentVsBudget = 4,

    /// <summary>Spec 048 / FR-010(a) — Warning: an evidence document dated after its payment, or
    /// before the agreement-execution date. Non-blocking.</summary>
    EvidenceDateAnomaly = 5,

    /// <summary>Spec 048 / FR-010(b) — Warning: the same supplier + amount + date appears across
    /// more than one non-cancelled disbursement (possible duplicate payment). Non-blocking.</summary>
    PossibleDuplicatePayment = 6,

    /// <summary>Spec 048 / FR-010(c) — Warning: a validated line payment drifts from the
    /// independently-allocated graph invoice for that line (047 FINDING-13). Non-blocking.</summary>
    GraphInvoiceAllocationDrift = 7,
}
