// Spec 021 — see specs/021-feedback-session-may13/tasks.md T104.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 021 / US3 / T104 — POM for the supplier-admin surfaces. Wraps the
/// shared <c>/Admin/Suppliers</c> screens (Index, Detail) with the new
/// spec-021 widgets:
///   - search box (Name + CédulaJurídica autocomplete, T108 + T109)
///   - Process filter dropdown (FR-011)
///   - LastUsedAt column header (FR-011)
///   - sidebar variant testid (T111) when the caller holds only SupplierAdmin
///   - 403 error page (T112) when reaching denied admin routes
///
/// Locators use stable English <c>data-testid</c> slugs per the NFR-001
/// convention; visible Spanish labels remain free to change.
/// </summary>
public class SupplierAdminPage : AdminBasePage
{
    public SupplierAdminPage(IPage page) : base(page)
    {
    }

    // ---------- Sidebar ----------

    /// <summary>Spec 021 / T111 — narrowed sidebar variant marker.</summary>
    public ILocator SidebarVariant =>
        Page.Locator("[data-testid=\"sidebar-supplier-admin-variant\"]");

    /// <summary>The single allowed entry on the SupplierAdmin sidebar.</summary>
    public ILocator SidebarSuppliersEntry =>
        Page.Locator("[data-testid=\"sidebar-entry-supplier-admin-suppliers\"]");

    /// <summary>Negative selector — the full admin section header is hidden.</summary>
    public ILocator AdminSectionHeader =>
        Page.Locator("[data-section-testid=\"admin-section\"]");

    // ---------- Suppliers Index ----------

    public ILocator SuppliersArea =>
        Page.Locator("[data-testid=\"admin-suppliers-area\"]");
    public ILocator SearchInput =>
        Page.Locator("[data-testid=\"admin-suppliers-search-input\"]");
    // Fondo → Proceso cascading drill-down (shared component). The process level.
    public ILocator FundFilter =>
        Page.Locator("[data-testid=\"admin-suppliers-cascade-fund\"]");
    public ILocator ProcessFilter =>
        Page.Locator("[data-testid=\"admin-suppliers-cascade-process\"]");
    public ILocator LastUsedColumnHeader =>
        Page.Locator("[data-testid=\"admin-suppliers-col-last-used\"]");
    public ILocator FilterForm =>
        Page.Locator("[data-testid=\"admin-suppliers-filter-form\"]");
    public ILocator SuppliersTable =>
        Page.Locator("[data-testid=\"admin-suppliers-table\"]");
    public ILocator AutocompleteResults =>
        Page.Locator("[data-supplier-autocomplete-results]");

    // ---------- 403 ----------

    public ILocator Error403Page =>
        Page.Locator("[data-testid=\"error-403-page\"]");
    public ILocator Error403BackLink =>
        Page.Locator("[data-testid=\"error-403-back-link\"]");

    // ---------- Navigation ----------

    public Task GoToIndexAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Suppliers");

    public Task GoToDirectAsync(string baseUrl, string adminPath) =>
        Page.GotoAsync($"{baseUrl}{adminPath}");

    // ---------- Actions ----------

    public async Task SearchAsync(string term)
    {
        await SearchInput.FillAsync(term);
        await FilterForm.Locator("button[type=submit]").ClickAsync();
    }
}
