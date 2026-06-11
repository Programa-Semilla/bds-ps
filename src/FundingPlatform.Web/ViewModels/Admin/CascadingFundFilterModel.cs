using FundingPlatform.Application.Admin.Filters;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>How deep the cascading Fondo → Proceso → Grupo filter renders.</summary>
public enum CascadeFilterDepth
{
    /// <summary>Only the Fondo select.</summary>
    Fund = 1,

    /// <summary>Fondo → Proceso.</summary>
    Process = 2,

    /// <summary>Fondo → Proceso → Grupo.</summary>
    Group = 3,
}

/// <summary>
/// Model for the shared <c>_CascadingFundFilter</c> partial — a single-select
/// per level cascading filter (each level has a "Todos…" option). Selecting a
/// Fund narrows the Process options; selecting a Process narrows the Group
/// options. Each level posts its own GET field; an empty value means "all".
/// Field names are configurable so each surface keeps its existing query keys.
/// </summary>
public sealed class CascadingFundFilterModel
{
    public IReadOnlyList<FundHierarchyNode> Funds { get; init; } = Array.Empty<FundHierarchyNode>();

    public CascadeFilterDepth Depth { get; init; } = CascadeFilterDepth.Group;

    public string FundFieldName { get; init; } = "fundFilter";
    public string ProcessFieldName { get; init; } = "processFilter";
    public string GroupFieldName { get; init; } = "groupFilter";

    public int? SelectedFundId { get; init; }
    public int? SelectedProcessId { get; init; }
    public int? SelectedGroupId { get; init; }

    /// <summary>Prefix for element ids + data-testids so several instances never
    /// collide and tests can target a specific surface.</summary>
    public string IdPrefix { get; init; } = "cascade-filter";

    /// <summary>
    /// When <c>true</c>, renders as a REQUIRED selector for an editing/creating
    /// form rather than a table filter: the empty option reads "Seleccione un …"
    /// (not "Todos los …") and each level is HTML5-<c>required</c> so the browser
    /// blocks submission until a real value is chosen. Default <c>false</c>
    /// (filter mode, where the empty "Todos" value means "all").
    /// </summary>
    public bool RequireSelection { get; init; }

    /// <summary>Optional Bootstrap column class for each select wrapper
    /// (e.g. <c>col-md-3</c>); empty renders the selects inline without columns.</summary>
    public string ColumnClass { get; init; } = string.Empty;

    public bool ShowProcess => Depth >= CascadeFilterDepth.Process;
    public bool ShowGroup => Depth >= CascadeFilterDepth.Group;
}
