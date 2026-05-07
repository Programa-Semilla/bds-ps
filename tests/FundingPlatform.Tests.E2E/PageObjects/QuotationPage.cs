using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class QuotationPage : BasePage
{
    public QuotationPage(IPage page) : base(page)
    {
    }

    public ILocator PriceInput => Page.Locator("[name=Price]");

    /// <summary>
    /// Spec 015 / T113 — the currency control is now a &lt;select&gt; populated from
    /// the enabled-currencies catalog. The PageObject's <c>FillQuotationFormAsync</c>
    /// detects either tag and dispatches accordingly so existing tests keep working
    /// even before the catalog is seeded (free-text fallback).
    /// </summary>
    public ILocator CurrencyInput => Page.Locator("[name=Currency]");
    public ILocator ValidUntilInput => Page.Locator("[name=ValidUntil]");
    public ILocator QuotationFileInput => Page.Locator("[name=QuotationFile]");
    public ILocator SubmitButton => Page.Locator("main button[type=submit]");
    public ILocator ValidationSummary => Page.Locator(".text-danger");
    public ILocator ConversionPreview => Page.Locator("[data-quote-preview]");
    public ILocator PreviewAmount => Page.Locator("[data-preview-amount]");
    public ILocator PreviewRate => Page.Locator("[data-preview-rate]");
    public ILocator PreviewStatus => Page.Locator("[data-preview-status]");

    public async Task NavigateToAddAsync(int appId, int itemId, int supplierId, string supplierName, string baseUrl)
    {
        var encodedName = Uri.EscapeDataString(supplierName);
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/{itemId}/Quotation/Add?supplierId={supplierId}&supplierName={encodedName}");
    }

    public async Task FillQuotationFormAsync(decimal price, string validUntil, string filePath, string? currency = null)
    {
        await PriceInput.FillAsync(price.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (currency is not null) await SetCurrencyAsync(currency);
        await ValidUntilInput.FillAsync(validUntil);
        await QuotationFileInput.SetInputFilesAsync(filePath);
    }

    public async Task SetCurrencyAsync(string currency)
    {
        var tag = (await CurrencyInput.EvaluateAsync<string>("el => el.tagName")).ToUpperInvariant();
        if (tag == "SELECT")
        {
            await CurrencyInput.SelectOptionAsync(currency);
        }
        else
        {
            await CurrencyInput.FillAsync(currency);
        }
    }

    public Task<string> ReadCurrencyValueAsync() => CurrencyInput.InputValueAsync();

    public async Task SubmitAsync()
    {
        await SubmitButton.ClickAsync();
    }
}
