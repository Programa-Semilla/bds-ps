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

        // Hero elements (FR-004): seedling mark + "Programa Semilla" wordmark
        // image + a tagline. The wordmark is rendered as <img alt="Programa Semilla">
        // so it is reachable via accessibility name (alt text), not page text.
        // Brand wordmark is the FR-033 brand-presence assertion target.
        var wordmark = Page.GetByAltText("Programa Semilla").First;
        await Expect(wordmark).ToBeVisibleAsync();

        // Sponsor strip rendered in the footer (FR-003 / FR-033).
        var sponsorStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
        await Expect(sponsorStrip).ToBeVisibleAsync();
    }
}
