using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T057 / FR-032 — Admin index POM with semantic locators.
/// </summary>
public class AdminIndexPage
{
    private readonly IPage _page;

    public AdminIndexPage(IPage page)
    {
        _page = page;
    }

    public Task GotoAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Admin");

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator KpiTiles => _page.Locator(".fl-kpi-tile");
    public ILocator CapabilityCards => _page.Locator("[data-testid^=\"admin-capability-\"]");
    public ILocator AccentDividers => _page.Locator(".fl-divider-accent");
    public ILocator ActivityFeed => _page.Locator("[data-testid=\"admin-activity-feed\"]");
}
