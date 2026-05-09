using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T048 / FR-032 — Reviewer detail POM with semantic locators.
/// </summary>
public class ReviewerDetailPage
{
    private readonly IPage _page;

    public ReviewerDetailPage(IPage page)
    {
        _page = page;
    }

    public Task GotoAsync(string baseUrl, string applicationId) =>
        _page.GotoAsync($"{baseUrl}/Review/{applicationId}");

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator ApproveButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new("(?i)aprobar|approve") });
    public ILocator SendBackButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new("(?i)devolver|send.?back") });
    public ILocator ItemTable => _page.Locator("table[data-density=\"reviewer\"]").First;
}
