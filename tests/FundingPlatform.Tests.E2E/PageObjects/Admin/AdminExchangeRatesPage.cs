using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 015 / US3 / T303 — page object for the admin exchange-rate history
/// list at <c>/Admin/ExchangeRates</c>.
/// </summary>
public class AdminExchangeRatesPage : AdminBasePage
{
    public AdminExchangeRatesPage(IPage page) : base(page) { }

    public ILocator Table => Page.Locator("[data-testid=\"admin-exchange-rates-table\"]");
    public new ILocator EmptyState => Page.Locator("[data-testid=\"admin-exchange-rates-empty\"]");
    public ILocator CreateButton => Page.Locator("[data-testid=\"admin-exchange-rates-create-button\"]");
    public ILocator SuccessBanner => Page.Locator("[data-testid=\"success-banner\"]");
    public ILocator ErrorBanner => Page.Locator("[data-testid=\"error-banner\"]");
    public ILocator AnyRow => Page.Locator("[data-testid^=\"admin-exchange-rate-row-\"]");
    public ILocator ActiveBadges => Page.Locator("[data-testid=\"rate-active-badge\"]");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/ExchangeRates");

    public Task GoToCreateAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/ExchangeRates/Create");
}

/// <summary>
/// Companion page object for the rate creation form.
/// </summary>
public class AdminExchangeRateCreatePage : AdminBasePage
{
    public AdminExchangeRateCreatePage(IPage page) : base(page) { }

    public ILocator Form => Page.Locator("[data-testid=\"admin-exchange-rate-create-form\"]");
    public ILocator SourceSelect => Page.Locator("[data-testid=\"source-currency-select\"]");
    public ILocator TargetSelect => Page.Locator("[data-testid=\"target-currency-select\"]");
    public ILocator BuyInput => Page.Locator("[data-testid=\"buy-rate-input\"]");
    public ILocator SellInput => Page.Locator("[data-testid=\"sell-rate-input\"]");
    public ILocator EffectiveAtInput => Page.Locator("[data-testid=\"effective-at-input\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"admin-exchange-rate-submit\"]");
    public ILocator ValidationSummary =>
        Page.Locator("[data-testid=\"validation-summary\"], .text-danger");

    public async Task FillAsync(
        string source, string target, string buy, string sell, string effectiveLocal)
    {
        await SourceSelect.SelectOptionAsync(source);
        await TargetSelect.SelectOptionAsync(target);
        await BuyInput.FillAsync(buy);
        await SellInput.FillAsync(sell);
        await EffectiveAtInput.FillAsync(effectiveLocal);
    }

    public Task SubmitAsync() => SubmitButton.ClickAsync();
}
