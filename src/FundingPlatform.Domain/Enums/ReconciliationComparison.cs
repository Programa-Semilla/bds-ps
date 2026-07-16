namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-011 — identifies which of the three reconciliation comparisons
/// produced a <see cref="ValueObjects.ReconciliationDiscrepancy"/>.
/// </summary>
public enum ReconciliationComparison : byte
{
    /// <summary>(a) Disbursement amount vs bank receipt amount.</summary>
    DisbursementVsBankReceipt = 0,

    /// <summary>(b) Disbursement amount vs invoice amount.</summary>
    DisbursementVsInvoice = 1,

    /// <summary>(c) Sum of non-cancelled disbursements vs the executed agreement total.</summary>
    TotalVsAllocation = 2,
}
