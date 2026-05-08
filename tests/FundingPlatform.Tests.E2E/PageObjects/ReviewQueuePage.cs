using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ReviewQueuePage : BasePage
{
    public ReviewQueuePage(IPage page) : base(page)
    {
    }

    // Spec 011 facelift: queue is rendered inside [data-testid=reviewer-queue] in
    // QueueDashboard.cshtml; rows carry [data-testid=reviewer-queue-row] (one per app).
    public ILocator QueueTable => Page.Locator("[data-testid=reviewer-queue] table");
    public ILocator QueueRows => Page.Locator("[data-testid=reviewer-queue-row]");
    public ILocator PaginationLinks => Page.Locator("nav.pagination a");
    public ILocator PageInfo => Page.Locator(".page-info");
    public ILocator NoApplicationsMessage => Page.Locator(".alert:has-text('No hay solicitudes')");

    public ILocator ReviewTabs => Page.Locator("[data-testid=review-tabs]");
    public ILocator InitialQueueTab => Page.Locator("[data-testid=review-tab-initial]");
    public ILocator SigningInboxTab => Page.Locator("[data-testid=review-tab-signing]");

    public async Task GotoAsync(string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Review");
    }

    public ILocator ReviewLink(int applicationId)
    {
        // End-anchored: rejects "/Review/Review/{id}" (substring match would accept it).
        return Page.Locator($"a[href$='/Review/{applicationId}']");
    }

    public ILocator RowActionFor(int applicationId)
    {
        // The queue-row "Revisar" button — end-anchored on the application route.
        return Page.Locator($"[data-testid=row-action][href$='/Review/{applicationId}']");
    }

    public async Task<int> GetQueueCountAsync()
    {
        return await QueueRows.CountAsync();
    }

    public async Task ClickSigningInboxTab()
    {
        await SigningInboxTab.ClickAsync();
    }

    public async Task<bool> IsSigningInboxTabActive()
    {
        var classAttr = await SigningInboxTab.GetAttributeAsync("class");
        return classAttr is not null && classAttr.Contains("active");
    }

    public async Task<bool> IsInitialQueueTabActive()
    {
        var classAttr = await InitialQueueTab.GetAttributeAsync("class");
        return classAttr is not null && classAttr.Contains("active");
    }

    public ILocator GenerateAgreementTab => Page.Locator("[data-testid=review-tab-generate]");
    public ILocator GenerateAgreementTable => Page.Locator("[data-testid=generate-agreement-table]");
    public ILocator GenerateAgreementRows => Page.Locator("[data-testid=generate-agreement-row]");
    public ILocator GenerateAgreementEmpty => Page.Locator("[data-testid=generate-agreement-empty]");

    public async Task ClickGenerateAgreementTab()
    {
        await GenerateAgreementTab.ClickAsync();
    }

    public async Task<bool> IsGenerateAgreementTabActive()
    {
        var classAttr = await GenerateAgreementTab.GetAttributeAsync("class");
        return classAttr is not null && classAttr.Contains("active");
    }

    public async Task GotoGenerateAgreementAsync(string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Review/GenerateAgreement");
    }

    // Spec 016 / FR-014 — search input on the queue.
    public ILocator SearchInput => Page.Locator("[data-testid=reviewer-queue-search-input]");
    public ILocator SearchSubmit => Page.Locator("[data-testid=reviewer-queue-search-submit]");

    public async Task SearchAsync(string term)
    {
        await SearchInput.FillAsync(term);
        await SearchSubmit.ClickAsync();
    }
}
