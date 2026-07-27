namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 048 / research D2 — the entity a
/// <see cref="FundingPlatform.Domain.Entities.Discrepancy"/> is scoped to. The polymorphic
/// <c>ScopeEntityId</c> holds the id of that entity (no FK — the rows are engine-managed and always
/// recomputed from live data, so a stale scope id simply auto-resolves next run; this avoids the
/// multiple-cascade-path dacpac publish failure that 5 typed FKs would cause — spec-029/035 lesson).
///
/// Stored as <c>TINYINT</c> — the EF mapping MUST use <c>HasConversion&lt;byte&gt;()</c>.
/// </summary>
public enum DiscrepancyScopeType : byte
{
    /// <summary><c>ScopeEntityId</c> holds an <c>EvidenceId</c>.</summary>
    Document = 0,

    /// <summary><c>ScopeEntityId</c> holds a <c>DisbursementId</c>.</summary>
    Payment = 1,

    /// <summary><c>ScopeEntityId</c> holds an <c>ItemId</c> (budget-line).</summary>
    BudgetLine = 2,

    /// <summary><c>ScopeEntityId</c> holds an <c>ApplicationId</c> (participant-level).</summary>
    Participant = 3,

    /// <summary><c>ScopeEntityId</c> holds a <c>TrancheId</c>.</summary>
    Tranche = 4,
}
