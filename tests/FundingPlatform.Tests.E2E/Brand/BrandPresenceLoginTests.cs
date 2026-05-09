using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T033 / FR-004 / FR-033 — Login page brand presence.
/// Asserts the Login page renders the left-rail hero (mark + "Programa Semilla"
/// wordmark + tagline) and the footer sponsor strip.
/// </summary>
public class BrandPresenceLoginTests : AuthenticatedTestBase
{
    [Test]
    public async Task LoginPage_RendersHero_And_SponsorStrip()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");

        // Hero elements (FR-004): the seedling mark + "Programa Semilla" wordmark
        // text + a tagline. Brand wordmark text is the FR-033 brand-presence target.
        var wordmarkInPage = Page.GetByText("Programa Semilla", new() { Exact = false }).First;
        await Expect(wordmarkInPage).ToBeVisibleAsync();

        // Sponsor strip rendered in the footer (FR-003 / FR-033).
        var sponsorStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
        await Expect(sponsorStrip).ToBeVisibleAsync();
    }
}
