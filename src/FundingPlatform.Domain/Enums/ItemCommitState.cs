namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 046 / FR-009 (research D1/D2) — the Financial Operator's off-ledger commit
/// status on a budget-line (<see cref="Entities.Item"/>). A commitment is an
/// obligation, not settled cash, so it is a mutable operational status — NOT a
/// <see cref="LedgerEntryType"/> entry (the append-only ledger stays "settled cash
/// only"). Stored as <c>dbo.Items.CommitState TINYINT NOT NULL DEFAULT (0)</c>, so
/// every pre-P2 row is <see cref="Uncommitted"/> with no backfill (spec 032/037
/// nullable-safe column-add precedent). EF must map it <c>HasConversion&lt;byte&gt;()</c>
/// or real-SQL materialization throws <c>Byte→Int32</c> (035/040/045 lesson).
/// </summary>
public enum ItemCommitState : byte
{
    /// <summary>Default — the line has not been obligated; it cannot accept payment attributions.</summary>
    Uncommitted = 0,

    /// <summary>The Financial Operator has obligated the line; attributions are now accepted.
    /// Reversible via <see cref="Entities.Item.Uncommit"/> until the first payment lands.</summary>
    Committed = 1,
}
