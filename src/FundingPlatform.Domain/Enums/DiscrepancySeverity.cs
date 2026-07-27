namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 045 / FR-015 — the severity of a reconciliation discrepancy.
/// <see cref="Blocking"/> prevents validate/close until resolved; spec 048 activates the reserved
/// P4 seam <see cref="Warning"/> — a non-blocking, waivable discrepancy tier. Severity is fixed
/// per <see cref="ReconciliationComparison"/> (comparisons 0–4 → Blocking; 5–7 → Warning) by the
/// spec-048 materializer; the tier is the extensible seam for later slices to register more rules.
/// </summary>
public enum DiscrepancySeverity : byte
{
    /// <summary>Blocks validation/closure until resolved.</summary>
    Blocking = 0,

    /// <summary>Spec 048 — a non-blocking discrepancy; surfaced and tracked, but never blocks a
    /// money gate. Deliberately accepted via <see cref="DiscrepancyState.Waived"/> (reason required).</summary>
    Warning = 1,
}
