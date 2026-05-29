using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public abstract class BasePage
{
    protected readonly IPage Page;

    protected BasePage(IPage page)
    {
        Page = page;
    }

    public ILocator Sidebar => Page.Locator("[data-testid=\"sidebar\"]");
    public ILocator Topbar => Page.Locator("[data-testid=\"topbar\"]");
    public ILocator PageTitle => Page.Locator("[data-testid=\"page-title\"]");
    public ILocator BreadcrumbContainer => Page.Locator("[data-testid=\"breadcrumbs\"]");

    public ILocator SidebarEntry(string slug) => Page.Locator($"[data-testid=\"sidebar-entry-{slug}\"]");

    /// <summary>
    /// Sidebar section headers (admin-section / proceso-section) are accordion
    /// toggles with no navigation: clicking expands the group and reveals its
    /// children (accordion — opening one collapses the other). Idempotent: a
    /// no-op when the section is already expanded.
    /// </summary>
    public ILocator SidebarSectionHeader(string section) => Page.Locator($"[data-section-testid=\"{section}\"]");

    public async Task ExpandSidebarSectionAsync(string section)
    {
        var header = SidebarSectionHeader(section);
        var expanded = await header.GetAttributeAsync("aria-expanded");
        if (!string.Equals(expanded, "true", StringComparison.OrdinalIgnoreCase))
        {
            await header.ClickAsync();
        }
    }

    // Spec 024 — toast + shared confirmation modal surfaces.
    public ILocator SuccessToast => Page.Locator("[data-testid=\"success-banner\"]");
    public ILocator ErrorToast => Page.Locator("[data-testid=\"error-banner\"]");
    public ILocator SharedConfirmModal => Page.Locator("#fl-shared-confirm-modal");
    public ILocator SharedConfirmButton => Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]");
    public ILocator SharedConfirmCancel => Page.Locator("#fl-shared-confirm-modal [data-testid=\"cancel-button\"]");

    /// <summary>
    /// Spec 024 — destructive actions open the shared confirm modal; click its
    /// confirm button to proceed (replaces the old native confirm() dialog accept).
    /// </summary>
    public async Task ConfirmInModalAsync()
    {
        await SharedConfirmButton.ClickAsync();
    }
}
