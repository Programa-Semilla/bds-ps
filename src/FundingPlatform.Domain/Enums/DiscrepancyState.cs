namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 048 / FR-006 — the lifecycle state of a persisted
/// <see cref="FundingPlatform.Domain.Entities.Discrepancy"/>. A discrepancy is detected
/// <see cref="Open"/>, may be worked (<see cref="Assigned"/> → <see cref="UnderCorrection"/>),
/// and reaches a terminal-ish rung either automatically (<see cref="Resolved"/>, when the numbers
/// match again) or deliberately (<see cref="Waived"/>, a non-blocking Warning accepted with a
/// reason). Both terminal rungs re-open automatically on recurrence.
///
/// Stored as <c>TINYINT</c> — the EF mapping MUST use <c>HasConversion&lt;byte&gt;()</c> or
/// real-SQL materialization throws <c>Byte→Int32</c> (the 035/040/045 lesson; InMemory hides it).
/// </summary>
public enum DiscrepancyState : byte
{
    /// <summary>Detected, unassigned.</summary>
    Open = 0,

    /// <summary>Assigned to a responsible operator.</summary>
    Assigned = 1,

    /// <summary>The operator is actively correcting the underlying data.</summary>
    UnderCorrection = 2,

    /// <summary>Cleared — the numbers match (auto) or a warning's condition no longer holds.</summary>
    Resolved = 3,

    /// <summary>A <see cref="DiscrepancySeverity.Warning"/> deliberately accepted (reason required);
    /// never valid for <see cref="DiscrepancySeverity.Blocking"/>.</summary>
    Waived = 4,
}
