using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 046 / US1 — drives the tranche (Tramos) editor rendered on the reviewer review surface
/// (<c>/Review/{id}</c>) pre-audit. Forms POST and redirect back to the review page.
/// </summary>
public sealed class TranchePage : BasePage
{
    public TranchePage(IPage page) : base(page) { }

    public ILocator Editor => Page.Locator("[data-testid=tranche-editor]");
    public ILocator TrancheRows => Page.Locator("[data-testid=tranche-row]");
    public ILocator Synthetic => Page.Locator("[data-testid=tranche-synthetic]");
    public ILocator AllocationTotal => Page.Locator("[data-testid=tranche-allocation-total]");
    public ILocator LineRows => Page.Locator("[data-testid=line-assign-row]");

    public async Task GotoReviewAsync(string baseUrl, int applicationId)
        => await Page.GotoAsync($"{baseUrl}/Review/{applicationId}");

    public async Task<string> FirstLineItemIdAsync()
        => (await LineRows.First.GetAttributeAsync("data-item-id"))!;

    public async Task CreateTrancheAsync(string name)
    {
        await Page.Locator("[data-testid=tranche-name-input]").FillAsync(name);
        await Page.Locator("[data-testid=tranche-create-submit]").ClickAsync();
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/Review/\d+"));
    }

    public async Task AssignLineToTrancheByLabelAsync(string itemId, string trancheName)
    {
        var row = Page.Locator($"[data-testid=line-assign-row][data-item-id=\"{itemId}\"]");
        await row.Locator("[data-testid=line-assign-select]")
            .SelectOptionAsync(new SelectOptionValue { Label = trancheName });
        await row.Locator("[data-testid=line-assign-submit]").ClickAsync();
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/Review/\d+"));
    }

    /// <summary>Derived-amount cell text of the first tranche row.</summary>
    public ILocator FirstTrancheDerivedAmount
        => TrancheRows.First.Locator("[data-testid=tranche-derived-amount]");

    /// <summary>The synthetic ("General") tranche's derived-amount cell.</summary>
    public ILocator SyntheticAmount => Page.Locator("[data-testid=tranche-synthetic-amount]");
}
