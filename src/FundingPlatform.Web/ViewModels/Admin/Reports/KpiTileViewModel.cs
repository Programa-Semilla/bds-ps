using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Web.ViewModels.Admin.Reports;

/// <summary>
/// Spec 010 + spec 017 (US6 / FR-022) — single KPI tile contract.
/// <see cref="Href"/> + <see cref="Slug"/> were added in spec 017 so the admin
/// dashboard's KPI strip can deep-link tiles. Existing report-tab callers omit
/// both and render unchanged.
/// </summary>
public sealed record KpiTileViewModel(
    string Label,
    int? NumericValue,
    IReadOnlyList<CurrencyAmount>? Stack,
    string? Href = null,
    string? Slug = null);
