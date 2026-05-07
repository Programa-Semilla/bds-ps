using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 015 / T102 — page object for the supplier-quote Add form when an applicant
/// has already picked a supplier and is on
/// <c>/Application/{appId}/Item/{itemId}/Quotation/Add</c>. The richer multi-step
/// supplier-search flow lives in <see cref="SupplierPage"/>; this POM is the
/// single-step "I already know my supplier, just enter the quote" surface.
/// </summary>
public class AddQuotationPage : BasePage
{
    public AddQuotationPage(IPage page) : base(page) { }

    public ILocator PriceInput => Page.Locator("[name=Price]");
    public ILocator CurrencySelect => Page.Locator("select[name=Currency]");
    public ILocator CurrencyControl => Page.Locator("[name=Currency]");
    public ILocator ValidUntilInput => Page.Locator("[name=ValidUntil]");
    public ILocator QuotationFileInput => Page.Locator("[name=QuotationFile]");
    public ILocator SubmitButton => Page.Locator("button[type=submit]:has-text('Agregar cotización')");
    public ILocator ValidationSummary => Page.Locator(".text-danger");
    public ILocator ConversionPreview => Page.Locator("[data-quote-preview]");
    public ILocator PreviewAmount => Page.Locator("[data-preview-amount]");
    public ILocator PreviewRate => Page.Locator("[data-preview-rate]");
    public ILocator PreviewStatus => Page.Locator("[data-preview-status]");

    public async Task GotoAsync(string baseUrl, int appId, int itemId, int supplierId, string supplierName)
    {
        var encodedName = Uri.EscapeDataString(supplierName);
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/{itemId}/Quotation/Add?supplierId={supplierId}&supplierName={encodedName}");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    public async Task SetCurrencyAsync(string code)
    {
        var tag = (await CurrencyControl.EvaluateAsync<string>("el => el.tagName")).ToUpperInvariant();
        if (tag == "SELECT")
        {
            await CurrencyControl.SelectOptionAsync(code);
            // Manually fire change so any input handlers (and the preview JS) wake up
            // even when SelectOptionAsync doesn't bubble it (Playwright does, but the
            // explicit dispatch is a safe redundancy under shared-fixture flake).
            await CurrencyControl.DispatchEventAsync("change");
        }
        else
        {
            await CurrencyControl.FillAsync(code);
            await CurrencyControl.DispatchEventAsync("change");
        }
    }

    public async Task FillFormAsync(decimal price, string currency, string validUntil, string filePath)
    {
        await PriceInput.FillAsync(price.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await SetCurrencyAsync(currency);
        await ValidUntilInput.FillAsync(validUntil);
        await QuotationFileInput.SetInputFilesAsync(filePath);
    }

    public Task SubmitAsync() => SubmitButton.ClickAsync();
}
