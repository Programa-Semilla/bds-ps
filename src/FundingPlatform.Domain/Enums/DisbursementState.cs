namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-026 — the lifecycle state of a <see cref="Entities.Disbursement"/>.
/// Stored as <c>TINYINT</c> (EF <c>HasConversion&lt;byte&gt;()</c>).
/// <c>Recorded ⇄ Inconsistent</c> (reconciliation flips), then a terminal
/// <c>Validated</c> (via the explicit Validar action) or <c>Cancelled</c>
/// (from either pre-validation state).
/// </summary>
public enum DisbursementState : byte
{
    /// <summary>Recorded with no blocking discrepancy; validatable once both evidences are present.</summary>
    Recorded = 0,

    /// <summary>Has at least one blocking reconciliation discrepancy; not validatable until corrected.</summary>
    Inconsistent = 1,

    /// <summary>Explicitly validated; locked against edit/replace/cancel (terminal). A ledger entry is posted.</summary>
    Validated = 2,

    /// <summary>Cancelled before validation; contributes nothing to the balance (terminal).</summary>
    Cancelled = 3,
}
