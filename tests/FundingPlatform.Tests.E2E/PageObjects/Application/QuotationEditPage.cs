using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Application;

/// <summary>
/// Spec 023 — Page Object for the per-quotation Edit form
/// (<c>Quotation/Edit.cshtml</c>). Locators target the <c>data-testid</c>
/// hooks rendered by <c>_QuoteFields.cshtml</c> + <c>Quotation/Edit.cshtml</c>;
/// the same hooks are reused on <c>Supplier/Add.cshtml</c> so the partial
/// extraction is exercised by the existing Supplier/Add E2E suite.
/// </summary>
public class QuotationEditPage : BasePage
{
    public QuotationEditPage(IPage page) : base(page) { }

    public ILocator Heading => Page.Locator("[data-testid=quotation-edit-heading]");
    public ILocator PriceInput => Page.Locator("[data-testid=quotation-price-input]");
    public ILocator CurrencyInput => Page.Locator("[data-testid=quotation-currency-input]");
    public ILocator ValidUntilInput => Page.Locator("[data-testid=quotation-validuntil-input]");
    public ILocator BranchSelect => Page.Locator("[data-testid=quotation-branch-input]");
    public ILocator SubmitButton => Page.Locator("[data-testid=quotation-submit-button]");
    public ILocator CancelLink => Page.Locator("[data-testid=quotation-cancel-button]");
    public ILocator ValidationSummary => Page.Locator("[data-testid=quotation-edit-validation-summary]");
    public ILocator PriceError => Page.Locator("span[data-valmsg-for=Price]");
    public ILocator BranchError => Page.Locator("span[data-valmsg-for=SupplierBranchId]");

    public static ILocator EditButtonFor(IPage page, int quotationId)
        => page.Locator($"[data-testid=quotation-row-edit-{quotationId}]");

    public static ILocator RowFor(IPage page, int quotationId)
        => page.Locator($"[data-testid=quotation-row-{quotationId}]");

    public async Task FillPriceAsync(string value)
    {
        await PriceInput.FillAsync(value);
    }

    public async Task SetCurrencyAsync(string code)
    {
        await CurrencyInput.SelectOptionAsync(code);
    }

    public async Task SetBranchByValueAsync(string value)
    {
        await BranchSelect.SelectOptionAsync(value);
    }

    public async Task<IReadOnlyList<string>> GetBranchOptionValuesAsync()
    {
        var opts = await BranchSelect.Locator("option").AllAsync();
        var values = new List<string>(opts.Count);
        foreach (var o in opts)
        {
            var v = await o.GetAttributeAsync("value");
            if (!string.IsNullOrEmpty(v)) values.Add(v);
        }
        return values;
    }

    public Task SubmitAsync() => SubmitButton.ClickAsync();

    public async Task WaitForRedirectToApplicationEditAsync(int appId)
    {
        await Page.WaitForURLAsync(new Regex($"/Application/Edit/{appId}(?:[/?#]|$)"));
    }
}
