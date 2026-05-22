// Spec 021 — see specs/021-feedback-session-may13/tasks.md T142 and
// contracts/public-routes.md (Public landing) / spec FR-031.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 021 / US7 / T142 / FR-031 — POM for the anonymous public landing.
/// Encapsulates the hero CTA + button + the three FR-031 slot regions
/// (Reglamento, Ejemplo de cotización, Sponsor strip). Slot-region locators
/// expose both the "available" link and the "Próximamente" placeholder so
/// tests can assert either state without coupling to the rendered markup
/// inside each card.
/// </summary>
public sealed class PublicLandingPage : BasePage
{
    public PublicLandingPage(IPage page) : base(page) { }

    public Task GotoAsync(string baseUrl) => Page.GotoAsync($"{baseUrl.TrimEnd('/')}/");

    public ILocator Hero => Page.Locator("[data-testid=\"public-landing-hero\"]");
    public ILocator Cta => Page.Locator("[data-testid=\"public-landing-cta\"]");
    public ILocator CtaButton => Page.Locator("[data-testid=\"public-landing-cta-button\"]");

    public ILocator ReglamentoSlot => Page.Locator("[data-testid=\"public-landing-slot-reglamento\"]");
    public ILocator EjemploSlot => Page.Locator("[data-testid=\"public-landing-slot-ejemplo\"]");

    public ILocator ReglamentoLink => Page.Locator("[data-testid=\"public-landing-reglamento-link\"]");
    public ILocator EjemploLink => Page.Locator("[data-testid=\"public-landing-ejemplo-link\"]");

    public ILocator ReglamentoPlaceholder => Page.Locator("[data-testid=\"public-landing-reglamento-placeholder\"]");
    public ILocator EjemploPlaceholder => Page.Locator("[data-testid=\"public-landing-ejemplo-placeholder\"]");

    public ILocator SponsorStrip => Page.Locator("[data-testid=\"sponsor-strip\"]");
}
