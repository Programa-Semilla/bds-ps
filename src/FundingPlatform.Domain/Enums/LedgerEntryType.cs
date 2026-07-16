namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-017 — the type of an append-only
/// <see cref="Entities.DisbursementLedgerEntry"/>. In this slice the ledger has
/// exactly two entry types. Stored as <c>TINYINT</c> (EF <c>HasConversion&lt;byte&gt;()</c>).
/// P6 will add refund/reversal/credit-note/interest/fee types without a balance-logic rewrite.
/// </summary>
public enum LedgerEntryType : byte
{
    /// <summary>The participant's approved ceiling, snapshotted once at first disbursement.</summary>
    Allocation = 0,

    /// <summary>An immutable committed disbursement, posted at the moment of validation.</summary>
    Disbursement = 1,
}
