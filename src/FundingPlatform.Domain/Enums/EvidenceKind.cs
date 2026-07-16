namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-006 — the typed kind of a <see cref="Entities.DisbursementEvidence"/>.
/// Exactly one of each is required per disbursement before it can be validated.
/// Stored as <c>TINYINT</c> (EF <c>HasConversion&lt;byte&gt;()</c>).
/// </summary>
public enum EvidenceKind : byte
{
    /// <summary>The bank's proof that money moved (comparison a).</summary>
    BankReceipt = 0,

    /// <summary>The billed document justifying the payment (comparison b). FR-007: a contract does not substitute.</summary>
    Invoice = 1,
}
