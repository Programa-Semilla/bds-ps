using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 048 — a single non-blocking Warning surfaced by
/// <see cref="Services.ReconciliationWarnings"/>. Carries the scope + comparison + amounts the
/// materializer maps onto a persisted <see cref="Entities.Discrepancy"/> row (always
/// <see cref="DiscrepancySeverity.Warning"/>). Pure data — no behavior.
/// </summary>
public sealed record WarningDescriptor(
    DiscrepancyScopeType ScopeType,
    int ScopeEntityId,
    ReconciliationComparison Comparison,
    decimal Expected,
    decimal Actual,
    string SourceDocument);
