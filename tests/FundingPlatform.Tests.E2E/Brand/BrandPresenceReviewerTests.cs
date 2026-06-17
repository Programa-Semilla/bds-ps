using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T050 / FR-033 — Reviewer surfaces render brand sidebar + sponsor strip.
/// </summary>
public class BrandPresenceReviewerTests : AuthenticatedTestBase
{
    [Test]
    public async Task ReviewerQueue_RendersSidebarBrand_And_SponsorStrip()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"brand_rev_{unique}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Brand", "Rev", $"BR-{unique}");
        await AssignRoleAsync(email, "Reviewer");
        await LoginAsync(Page, email, password);

        await Page.GotoAsync($"{BaseUrl}/Review");

        var sidebarBrand = Page.Locator("[data-testid=\"sidebar-brand\"]");
        await Expect(sidebarBrand).ToBeVisibleAsync();
        await Expect(sidebarBrand).ToContainTextAsync(new Regex("Programa Semilla"));

        var sponsorStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
        await Expect(sponsorStrip).ToBeVisibleAsync();

        // Spec 037 FR-015 / FR-025 — official logo + dark-teal sidebar (#12343B).
        await Expect(sidebarBrand.Locator("img.fl-sidebar-brand-logo")).ToBeVisibleAsync();
        var sidebarBg = await Page.Locator("[data-testid=\"sidebar\"]")
            .EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.That(sidebarBg, Does.Match(@"rgb\(\s*18,\s*52,\s*59\s*\)"),
            $"Sidebar must be the official dark teal #12343B (got {sidebarBg}).");
    }

    [Test]
    public async Task ReviewerSigningInbox_RendersSidebarBrand_And_SponsorStrip()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"brand_rev_si_{unique}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Brand", "Rev", $"BR-{unique}");
        await AssignRoleAsync(email, "Reviewer");
        await LoginAsync(Page, email, password);

        await Page.GotoAsync($"{BaseUrl}/Review/SigningInbox");

        var sidebarBrand = Page.Locator("[data-testid=\"sidebar-brand\"]");
        await Expect(sidebarBrand).ToBeVisibleAsync();

        var sponsorStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
        await Expect(sponsorStrip).ToBeVisibleAsync();
    }
}
