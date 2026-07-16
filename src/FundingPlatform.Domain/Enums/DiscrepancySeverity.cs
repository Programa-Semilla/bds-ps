namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-015 — the severity of a <see cref="ValueObjects.ReconciliationDiscrepancy"/>.
/// Every discrepancy in this slice is <see cref="Blocking"/> (a <c>Warning</c> tier is
/// reserved for the P4 non-blocking discrepancy lifecycle). Single value in P1.
/// </summary>
public enum DiscrepancySeverity : byte
{
    /// <summary>Blocks validation until resolved.</summary>
    Blocking = 0,
}
