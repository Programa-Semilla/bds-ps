using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 037 FR-024 / SC-010 — at a narrow (mobile) viewport the new shell stays
/// usable: the filter card wraps, wide tables scroll horizontally, the footer
/// partner image scales down, and the sidebar collapses behind its toggler.
/// </summary>
public class ResponsiveLayoutTests : AuthenticatedTestBase
{
    private const int NarrowWidth = 390;

    private async Task SignInAdminAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.Locator("[name=Email]").FillAsync("admin@programa-semilla.test");
        await Page.Locator("[name=Password]").FillAsync("Sentinel123!");
        await Page.Locator("main button[type=submit]").ClickAsync();
    }

    [Test]
    public async Task NarrowViewport_FiltersWrap_TableScrolls_FooterScales_SidebarCollapses()
    {
        await Page.SetViewportSizeAsync(NarrowWidth, 844);
        await SignInAdminAsync();
        await Page.GotoAsync($"{BaseUrl}/Admin/Users");

        // Sidebar collapses behind its toggler below the lg breakpoint.
        var sidebarToggler = Page.Locator("[data-testid=\"sidebar\"] .navbar-toggler");
        await Expect(sidebarToggler).ToBeVisibleAsync();

        // Filter card form wraps its controls onto multiple rows (flex-wrap).
        var filterForm = Page.Locator("[data-testid=\"admin-users-filter-form\"]");
        var flexWrap = await filterForm.EvaluateAsync<string>("el => getComputedStyle(el).flexWrap");
        Assert.That(flexWrap, Is.EqualTo("wrap"), "Filter controls must wrap on a narrow viewport.");

        // The wide users table scrolls horizontally (its .table-responsive wrapper
        // allows overflow rather than clipping or overflowing the page).
        var responsive = Page.Locator("[data-testid=\"admin-users-table\"] .table-responsive");
        var overflowX = await responsive.EvaluateAsync<string>("el => getComputedStyle(el).overflowX");
        Assert.That(overflowX, Is.AnyOf("auto", "scroll"),
            $"Wide table must be horizontally scrollable on narrow viewports (overflow-x={overflowX}).");

        // Footer partner image scales within the viewport (width:100%, max-width cap).
        var sponsorImg = Page.Locator("[data-testid=\"sponsor-strip\"] img");
        await Expect(sponsorImg).ToBeVisibleAsync();
        var box = await sponsorImg.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Width, Is.LessThanOrEqualTo(NarrowWidth),
            $"Footer partner image must scale to the viewport (width {box.Width} > {NarrowWidth}).");
    }
}
