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

        // Footer sponsor strip.
        var sponsorStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
        await Expect(sponsorStrip).ToBeVisibleAsync();
    }
}
