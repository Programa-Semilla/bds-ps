namespace FundingPlatform.Web.Resources;

/// <summary>
/// Localized (es-CR) strings for the searchable-dropdown enhancer (spec 031).
/// The strings are surfaced to <c>searchable-select.js</c> through markup
/// (a layout-level <c>data-searchable-*</c> default and optional per-control
/// <c>data-searchable-placeholder</c>) so the JS module carries no Spanish
/// literals (FR-010, contracts/searchable-select.md §4).
/// </summary>
public static class SearchableDropdownResources
{
    /// <summary>Combobox input placeholder ("type to filter…").</summary>
    public const string SearchPlaceholder = "Escriba para filtrar…";

    /// <summary>Empty-state shown when no option matches the typed query.</summary>
    public const string NoMatchMessage = "Sin coincidencias";
}
