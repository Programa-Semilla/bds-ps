using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ItemPage : BasePage
{
    public ItemPage(IPage page) : base(page)
    {
    }

    public ILocator ProductNameInput => Page.Locator("[name=ProductName]");
    public ILocator CategorySelect => Page.Locator("[name=CategoryId]");
    public ILocator TechnicalSpecificationsInput => Page.Locator("[name=TechnicalSpecifications]");
    public ILocator SubmitButton => Page.Locator("main button[type=submit]");
    public ILocator ValidationSummary => Page.Locator(".validation-summary-errors, .field-validation-error");

    public async Task AddItemAsync(int appId, string productName, int categoryIndex, string techSpecs, string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/Add");
        await ProductNameInput.FillAsync(productName);

        var options = await CategorySelect.Locator("option").AllAsync();
        if (options.Count > categoryIndex + 1) // +1 to skip the "-- Select Category --" option
        {
            var value = await options[categoryIndex + 1].GetAttributeAsync("value");
            if (value is not null)
            {
                await CategorySelect.SelectOptionAsync(value);
            }
        }

        await TechnicalSpecificationsInput.FillAsync(techSpecs);
        await SubmitButton.ClickAsync();
    }

    public async Task EditItemAsync(int appId, int itemId, string productName, int categoryIndex, string techSpecs, string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/{itemId}/Edit");
        await ProductNameInput.ClearAsync();
        await ProductNameInput.FillAsync(productName);

        var options = await CategorySelect.Locator("option").AllAsync();
        if (options.Count > categoryIndex + 1)
        {
            var value = await options[categoryIndex + 1].GetAttributeAsync("value");
            if (value is not null)
            {
                await CategorySelect.SelectOptionAsync(value);
            }
        }

        await TechnicalSpecificationsInput.ClearAsync();
        await TechnicalSpecificationsInput.FillAsync(techSpecs);
        await SubmitButton.ClickAsync();
    }
}
