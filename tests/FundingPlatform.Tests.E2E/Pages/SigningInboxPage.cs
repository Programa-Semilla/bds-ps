using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T049 / FR-032 — Reviewer signing inbox POM with semantic locators.
/// </summary>
public class SigningInboxPage
{
    private readonly IPage _page;

    public SigningInboxPage(IPage page)
    {
        _page = page;
    }

    public Task GotoAsync(string baseUrl) => _page.GotoAsync($"{baseUrl}/Review/SigningInbox");

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator InboxTable => _page.Locator("table[data-density=\"reviewer\"]").First;
    public ILocator InboxRows => InboxTable.Locator("tbody tr");
    public ILocator SignModal => _page.Locator(".fl-modal");
    public ILocator SignModalHeader => _page.Locator(".fl-modal-header");
}
