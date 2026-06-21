using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>Spec 041 — POM for the funds-usage evidence inbox (/Evidence).</summary>
public sealed class EvidenceInboxPage : BasePage
{
    public EvidenceInboxPage(IPage page) : base(page) { }

    public ILocator Rows => Page.Locator("[data-testid=evidence-inbox-row]");
    public ILocator Empty => Page.Locator("[data-testid=evidence-inbox-empty]");

    public ILocator RowFor(string applicationNumber) =>
        Page.Locator($"[data-testid=evidence-inbox-row][data-application-number='{applicationNumber}']");

    public async Task GotoAsync(string baseUrl)
        => await Page.GotoAsync($"{baseUrl}/Evidence");

    public async Task<int> GotoStatusAsync(string baseUrl)
    {
        var resp = await Page.GotoAsync($"{baseUrl}/Evidence");
        return resp?.Status ?? 0;
    }

    /// <summary>Clicks the row's "Abrir" link → lands on the per-application evidence page.</summary>
    public async Task OpenAsync(string applicationNumber)
        => await RowFor(applicationNumber).Locator("[data-testid=evidence-inbox-open]").ClickAsync();
}
