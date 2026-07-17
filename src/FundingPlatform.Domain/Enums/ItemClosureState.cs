namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 047 / FR-015 (research D3) — the Financial Operator's off-ledger closure status on a
/// budget-line (<see cref="Entities.Item"/>). A line is <see cref="Closed"/> only when its
/// closure gate is satisfied (required docs present + payments validated + <c>LinePaid == LineAccepted</c>
/// to the colón + required evidence fully allocated). Closing writes NO ledger entry (off-ledger,
/// FR-018) and is reversible with a reason. Stored as <c>dbo.Items.ClosureState TINYINT NOT NULL
/// DEFAULT (0)</c>, so every pre-P3 row is <see cref="Open"/> with no backfill (spec 032/037/046
/// nullable-safe column-add precedent). EF must map it <c>HasConversion&lt;byte&gt;()</c> or real-SQL
/// materialization throws <c>Byte→Int32</c> (035/040/045/046 lesson).
/// </summary>
public enum ItemClosureState : byte
{
    /// <summary>Default — the line is still open; evidence can be attached/allocated and the line reopened is a no-op.</summary>
    Open = 0,

    /// <summary>The line has been closed (gate satisfied); evidence writes are locked until an audited reopen.</summary>
    Closed = 1,
}
