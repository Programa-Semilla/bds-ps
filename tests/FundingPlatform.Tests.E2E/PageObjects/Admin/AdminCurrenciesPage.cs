using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 015 / US3 / T302 — page object for the admin currency catalog list at
/// <c>/Admin/Currencies</c>.
/// </summary>
public class AdminCurrenciesPage : AdminBasePage
{
    public AdminCurrenciesPage(IPage page) : base(page) { }

    public ILocator Table => Page.Locator("[data-testid=\"admin-currencies-table\"]");
    public ILocator ErrorBanner => Page.Locator("[data-testid=\"error-banner\"]");
    public ILocator SuccessBanner => Page.Locator("[data-testid=\"success-banner\"]");

    public ILocator RowFor(string code) =>
        Page.Locator($"[data-testid=\"admin-currency-row-{code}\"]");

    public ILocator EnableButton(string code) =>
        RowFor(code).Locator("[data-testid=\"currency-enable-button\"]");

    public ILocator DisableButton(string code) =>
        RowFor(code).Locator("[data-testid=\"currency-disable-button\"]");

    public ILocator BaseLockedNotice(string code) =>
        RowFor(code).Locator("[data-testid=\"currency-base-locked\"]");

    public ILocator StatusBadge(string code, bool enabled) =>
        RowFor(code).Locator(enabled
            ? "[data-testid=\"currency-status-enabled\"]"
            : "[data-testid=\"currency-status-disabled\"]");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Currencies");

    public Task ClickEnableAsync(string code) => EnableButton(code).ClickAsync();
    public Task ClickDisableAsync(string code) => DisableButton(code).ClickAsync();
}
