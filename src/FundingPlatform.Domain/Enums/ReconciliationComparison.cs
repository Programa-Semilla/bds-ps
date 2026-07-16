namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-011 — identifies which reconciliation comparison produced a
/// <see cref="ValueObjects.ReconciliationDiscrepancy"/>. Spec 046 adds the two
/// line-level comparisons (research D6).
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
}
