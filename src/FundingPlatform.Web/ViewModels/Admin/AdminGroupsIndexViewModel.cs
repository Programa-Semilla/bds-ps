using FundingPlatform.Application.Admin.Filters;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 016 / 021 — view model for the admin Groups list (FR-003).</summary>
public sealed class AdminGroupsIndexViewModel
{
    public IReadOnlyList<AdminGroupRow> Groups { get; init; } = Array.Empty<AdminGroupRow>();

    /// <summary>True when the catalog has at least one Group regardless of the
    /// active filter — distinguishes "no groups yet" (CTA empty state) from
    /// "filters matched nothing".</summary>
    public bool HasAnyGroups { get; init; }

    /// <summary>Free-text name filter (null/empty = all).</summary>
    public string? Search { get; init; }

    /// <summary>Fondo → Proceso catalog (all Funds incl. Archived) for the
    /// cascading drill-down filter.</summary>
    public IReadOnlyList<FundHierarchyNode> FundHierarchy { get; init; }
        = Array.Empty<FundHierarchyNode>();

    /// <summary>Selected Fund filter (null = all).</summary>
    public int? FundFilter { get; init; }

    /// <summary>Selected Process filter (null = all).</summary>
    public int? ProcessFilter { get; init; }
}

/// <summary>Spec 021 / FR-001 + spec 029 — a Groups-index row carries the owning
/// Process and Fund (id + name) so the catalog surfaces both as clickable
/// columns.</summary>
public sealed record AdminGroupRow(
    int Id,
    string Name,
    int MemberCount,
    int ProcessId,
    string ProcessName,
    int FundId,
    string FundName);
