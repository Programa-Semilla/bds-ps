using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T056 / FR-033 / spec US3 — Admin index + every sub-surface
/// renders the brand sidebar (Programa Semilla wordmark) + sponsor strip.
/// KPI tile glow uses teal; Reports pill chip active state uses teal.
/// </summary>
public class BrandPresenceAdminTests : AuthenticatedTestBase
{
    private static readonly string[] AdminUrls =
    {
        "/Admin",
        "/Admin/Users",
        "/Admin/Groups",
        "/Admin/Suppliers",
        "/Admin/Reports",
        "/Admin/Currencies",
        "/Admin/ExchangeRates",
        "/Admin/LegacyQuotations",
        "/Admin/Configuration",
        "/Admin/ImpactTemplates",
    };

    [Test]
    public async Task AdminSurfaces_RenderSidebarBrand_And_SponsorStrip()
    {
        // Authenticate as admin (sentinel under ephemeral storage).
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.Locator("[name=Email]").FillAsync("admin@FundingPlatform.com");
        await Page.Locator("[name=Password]").FillAsync("Sentinel123!");
        await Page.Locator("main button[type=submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/$|/Admin"));

        foreach (var url in AdminUrls)
        {
            await Page.GotoAsync($"{BaseUrl}{url}");

            var sidebarBrand = Page.Locator("[data-testid=\"sidebar-brand\"]");
            await Expect(sidebarBrand).ToBeVisibleAsync();
            await Expect(sidebarBrand).ToContainTextAsync(new Regex("Programa Semilla"));

            var sponsorStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
            await Expect(sponsorStrip).ToBeVisibleAsync();
        }
    }
}
