using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Reconciliation;

/// <summary>
/// Spec 048 / US3 (FR-021–FR-024) — the group-scoped reconciliation dashboard read-model. Summary
/// tiles (count + amount by severity, roll-ups by fund/process), a filterable list of open/unresolved
/// discrepancies, and a scope-checked detail with the correction-history timeline. Group-scoping is
/// enforced in-query (admin short-circuit; group-overlap on the applicant's memberships; empty-group
/// non-admin → empty), mirroring <c>EvidenceInboxProjection</c>. Filtering follows the build-then-filter
/// pattern (base scoped set enriched, then facets applied in memory).
/// </summary>
public interface IReconciliationDashboardProjection
{
    Task<ReconciliationSummaryDto> GetSummaryAsync(IReviewerScope scope, ReconciliationFilter filter, CancellationToken ct);
    Task<IReadOnlyList<DiscrepancyRowDto>> GetDiscrepanciesAsync(IReviewerScope scope, ReconciliationFilter filter, CancellationToken ct);
    /// <summary>The one discrepancy + its event timeline, or null when out of scope (controller → flat 404).</summary>
    Task<DiscrepancyDetailDto?> GetDetailAsync(IReviewerScope scope, int discrepancyId, CancellationToken ct);
}

/// <summary>Spec 048 / FR-023 — the dashboard filter facets. <c>OpenOnly</c> (default) excludes
/// <see cref="DiscrepancyState.Resolved"/>; a specific <c>State</c> overrides it.</summary>
public sealed record ReconciliationFilter(
    int? ParticipantApplicationId = null,
    int? TrancheId = null,
    int? ItemId = null,
    int? SupplierId = null,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    DiscrepancySeverity? Severity = null,
    DiscrepancyState? State = null,
    string? ResponsibleUserId = null,
    bool OpenOnly = true);

/// <summary>Spec 048 — summary tiles + fund/process roll-ups (program/agency view).</summary>
public sealed record ReconciliationSummaryDto(
    int OpenBlockingCount,
    decimal OpenBlockingAmount,
    int OpenWarningCount,
    decimal OpenWarningAmount,
    IReadOnlyList<ReconciliationFundRollup> ByFund);

/// <summary>Spec 048 — per-fund roll-up of open discrepancy counts (agency view).</summary>
public sealed record ReconciliationFundRollup(
    string FundName,
    int OpenBlockingCount,
    int OpenWarningCount);

/// <summary>Spec 048 — one list row on the dashboard.</summary>
public sealed record DiscrepancyRowDto(
    int Id,
    int ApplicationId,
    string ApplicationNumber,
    string ParticipantName,
    DiscrepancyScopeType ScopeType,
    string ScopeLabel,
    ReconciliationComparison Comparison,
    DiscrepancySeverity Severity,
    DiscrepancyState State,
    decimal Expected,
    decimal Actual,
    decimal Difference,
    string SourceDocument,
    string? TrancheName,
    string? LineLabel,
    string? SupplierName,
    string? AssigneeName,
    DateOnly FirstDetected);

/// <summary>Spec 048 — the detail view: the row, the required action text, the event timeline, and
/// whether the current caller may act on it (Financial Operator only — Auditor/Admin read-only).</summary>
public sealed record DiscrepancyDetailDto(
    DiscrepancyRowDto Row,
    string RequiredAction,
    IReadOnlyList<DiscrepancyEventDto> Timeline,
    bool CanWrite);

/// <summary>Spec 048 — one correction-history timeline entry.</summary>
public sealed record DiscrepancyEventDto(
    DateTimeOffset OccurredAt,
    string Kind,
    DiscrepancyState FromState,
    DiscrepancyState ToState,
    string ActorName,
    string? Reason,
    string? Note);
