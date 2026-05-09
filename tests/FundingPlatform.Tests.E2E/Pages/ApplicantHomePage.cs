using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T035 / FR-032 — Page Object Model for the applicant home surface.
/// Locator strategy: ARIA role + accessible name first; data-testid fallback.
/// </summary>
public class ApplicantHomePage
{
    private readonly IPage _page;

    public ApplicantHomePage(IPage page)
    {
        _page = page;
    }

    public Task GotoAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Application");

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator MainHeading => _page.GetByRole(AriaRole.Heading).First;
    public ILocator CreateApplicationCta =>
        _page.GetByRole(AriaRole.Link, new() { NameRegex = new("(?i)nueva|new|start|empez")});
    public ILocator ApplicationsTable => _page.GetByRole(AriaRole.Table).First;
    public ILocator EmptyStateIllustration => _page.Locator(".fl-empty-illustration").First;
}
