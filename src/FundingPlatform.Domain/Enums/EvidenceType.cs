namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 047 / FR-002 (research D1) — the typed kind of an <see cref="Entities.Evidence"/>
/// node in the evidence graph. Distinct from P1's 2-valued <see cref="EvidenceKind"/> on
/// <see cref="Entities.DisbursementEvidence"/> (the money-gate), which is left untouched.
/// The graph hosts the payment-independent evidence — Signed Acceptance, Credit Note,
/// Refund Receipt, Other — plus optional supplementary bank receipts / invoices not tied
/// to a disbursement. Stored as <c>dbo.Evidence.Type TINYINT</c> (EF <c>HasConversion&lt;byte&gt;()</c>).
/// Credit Note and Refund Receipt are reconciliation-inert in P3 (FR-026) — placeholders for P6.
/// </summary>
public enum EvidenceType : byte
{
    /// <summary>The bank's proof that money moved. Supplementary to the disbursement money-gate.</summary>
    BankReceipt = 0,

    /// <summary>The billed document justifying the payment.</summary>
    Invoice = 1,

    /// <summary>The participant's signed acceptance of the goods/services. Reconciliation leg (FR-024):
    /// Σ acceptance allocations must equal Σ payments at closure.</summary>
    SignedAcceptance = 2,

    /// <summary>A credit note. Reconciliation-inert in P3 (FR-026); requirable via the matrix.</summary>
    CreditNote = 3,

    /// <summary>A refund receipt. Reconciliation-inert in P3 (FR-026); requirable via the matrix.</summary>
    RefundReceipt = 4,

    /// <summary>Any other supporting document.</summary>
    Other = 5,
}
