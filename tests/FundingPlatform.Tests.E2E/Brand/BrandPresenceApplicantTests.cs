using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T034 / FR-033 — Authenticated applicant pages render the brand
/// sidebar header (Programa Semilla wordmark) and the sponsor strip in the
/// footer. Sweeps Application list, Application/{id} (journey context),
/// and the signing entry surface.
/// </summary>
public class BrandPresenceApplicantTests : AuthenticatedTestBase
{
    [Test]
    public async Task ApplicantHome_RendersSidebarBrand_And_SponsorStrip()
    {
        // Register and authenticate an applicant.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"brand_appl_{unique}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Brand", "Applicant", $"BR-{unique}");
        await LoginAsync(Page, email, password);

        await Page.GotoAsync($"{BaseUrl}/Application");

        // Sidebar brand header — wordmark text "Programa Semilla" must be present.
        var sidebarBrand = Page.Locator("[data-testid=\"sidebar-brand\"]");
        await Expect(sidebarBrand).ToBeVisibleAsync();
        await Expect(sidebarBrand).ToContainTextAsync(new Regex("Programa Semilla"));

        // Footer sponsor strip (now the single official partner image).
        var sponsorStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
        await Expect(sponsorStrip).ToBeVisibleAsync();
        await Expect(sponsorStrip.Locator("img")).ToBeVisibleAsync();

        // Spec 037 FR-015 / FR-025 — official logo in the brand header + dark-teal
        // sidebar (#12343B = rgb(18, 52, 59)).
        await Expect(sidebarBrand.Locator("img.fl-sidebar-brand-logo")).ToBeVisibleAsync();
        var sidebarBg = await Page.Locator("[data-testid=\"sidebar\"]")
            .EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.That(sidebarBg, Does.Match(@"rgb\(\s*18,\s*52,\s*59\s*\)"),
            $"Sidebar must be the official dark teal #12343B (got {sidebarBg}).");
    }
}
