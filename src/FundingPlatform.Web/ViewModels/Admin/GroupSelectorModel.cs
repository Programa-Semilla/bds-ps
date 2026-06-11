namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Model for the shared <c>_GroupSelectorDrilldown</c> partial — the Fondo →
/// Proceso → Grupo drill-down used on the admin Create/Edit user forms. The
/// posted value is still the page model's <c>GroupIds</c> array (the partial
/// emits hidden inputs named <see cref="FieldName"/>); this model only carries
/// the catalog + the currently-selected groups needed to render the widget.
/// </summary>
public sealed class GroupSelectorModel
{
    /// <summary>Active-Fund drill-down catalog (Fondo → Proceso → Grupo).</summary>
    public IReadOnlyList<AdminUserFundCatalogOption> FundCatalog { get; init; }
        = Array.Empty<AdminUserFundCatalogOption>();

    /// <summary>Groups (id + name) currently assigned to the user — drives the
    /// initial chips + hidden inputs. May include groups under archived Funds
    /// that are absent from <see cref="FundCatalog"/>; those still render as
    /// removable chips so an existing membership is never silently dropped.</summary>
    public IReadOnlyList<AdminUserGroupOption> SelectedGroups { get; init; }
        = Array.Empty<AdminUserGroupOption>();

    /// <summary>Name of the posted form field (default <c>GroupIds</c>).</summary>
    public string FieldName { get; init; } = "GroupIds";

    /// <summary>True when there is at least one assignable group in the catalog
    /// or at least one already-selected group. When false the widget collapses
    /// to the empty state.</summary>
    public bool HasGroups =>
        SelectedGroups.Count > 0
        || FundCatalog.Any(f => f.Processes.Any(p => p.Groups.Count > 0));
}
