using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T047 / FR-032 — Reviewer queue POM with semantic locators.
/// </summary>
public class ReviewerQueuePage
{
    private readonly IPage _page;

    public ReviewerQueuePage(IPage page)
    {
        _page = page;
    }

    public Task GotoAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Review");

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator QueueTable => _page.Locator("table[data-density=\"reviewer\"]").First;
    public ILocator QueueRows => QueueTable.Locator("tbody tr");
    public ILocator FilterChips => _page.Locator(".fl-chip");
    public ILocator EmptyState => _page.Locator(".fl-empty");
}
