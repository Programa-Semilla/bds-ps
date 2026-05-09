using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T036 / FR-032 — Page Object Model for the applicant journey
/// timeline view. Semantic locators (ARIA role + accessible name); data-testid
/// only as fallback. Walks the spec-011 wow-moment journey timeline at the new bar.
/// </summary>
public class JourneyPage
{
    private readonly IPage _page;

    public JourneyPage(IPage page)
    {
        _page = page;
    }

    public Task GotoAsync(string baseUrl, string applicationId) =>
        _page.GotoAsync($"{baseUrl}/Application/{applicationId}");

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator JourneyTimeline => _page.Locator(".fl-journey").First;
    public ILocator JourneyNodes => _page.Locator(".fl-journey-node");
    public ILocator CurrentNode => _page.Locator(".fl-journey-node[data-state=\"current\"]");
    public ILocator EventLog => _page.GetByRole(AriaRole.List).Filter(new() { HasTextRegex = new("(?i)event|evento|hist") }).First;
}
