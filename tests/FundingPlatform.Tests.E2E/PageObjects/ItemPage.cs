using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 035 / US2 — POM for the category-first line-item form
/// (<c>/Application/{appId}/Item/Add</c> + <c>/{itemId}/Edit</c>). Selecting a
/// category AJAX-loads its dynamic field set; selecting an impact template
/// AJAX-loads its parameter set. Both render <c>input[data-dynamic-field]</c>
/// controls. <c>Item.TechnicalSpecifications</c> is gone.
/// </summary>
public class ItemPage : BasePage
{
    public ItemPage(IPage page) : base(page)
    {
    }

    public ILocator ProductNameInput => Page.Locator("[name=ProductName]");
    public ILocator CategorySelect => Page.Locator("#item-category-select");
    public ILocator ImpactSelect => Page.Locator("#item-impact-select");
    public ILocator CategoryFieldsContainer => Page.Locator("#category-fields");
    public ILocator ImpactParamsContainer => Page.Locator("#impact-params");
    public ILocator SubmitButton => Page.Locator("[data-testid=item-save]");
    public ILocator NoImpactTemplatesAlert => Page.Locator("[data-testid=item-no-impact-templates]");
    public ILocator ValidationSummary => Page.Locator(".text-danger.validation-summary-errors, .field-validation-error");

    /// <summary>
    /// Picks a category by 0-based real index (skipping the placeholder option),
    /// waits for the dynamic category-fields fetch, and fills every rendered field.
    /// </summary>
    public async Task SelectCategoryAndFillFieldsAsync(int categoryIndex = 0)
    {
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var options = await CategorySelect.Locator("option").AllAsync();
        var value = await options[categoryIndex + 1].GetAttributeAsync("value");

        await Page.RunAndWaitForResponseAsync(
            async () => await CategorySelect.SelectOptionAsync(value!),
            r => r.Url.Contains("/Item/Category/") && r.Url.Contains("/Fields"));

        await FillDynamicFieldsAsync(CategoryFieldsContainer);
    }

    /// <summary>
    /// Picks the first active impact template, waits for the parameter fetch, and
    /// fills every rendered parameter input.
    /// </summary>
    public async Task SelectImpactAndFillAsync()
    {
        var options = await ImpactSelect.Locator("option").AllAsync();
        var value = await options[1].GetAttributeAsync("value");

        await Page.RunAndWaitForResponseAsync(
            async () => await ImpactSelect.SelectOptionAsync(value!),
            r => r.Url.Contains("/Impact/TemplateParameters/"));

        await FillDynamicFieldsAsync(ImpactParamsContainer);
    }

    /// <summary>Fills every <c>input[data-dynamic-field]</c> in a container with a
    /// type-appropriate value (number / date / text).</summary>
    public async Task FillDynamicFieldsAsync(ILocator container)
    {
        var inputs = container.Locator("input[data-dynamic-field]");
        var count = await inputs.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var input = inputs.Nth(i);
            var type = await input.GetAttributeAsync("type");
            await input.FillAsync(type switch
            {
                "number" => "100",
                "date" => "2026-12-31",
                _ => "Valor de prueba",
            });
        }
    }

    /// <summary>
    /// Adds a line item. The <paramref name="techSpecs"/> parameter is retained
    /// for call-site compatibility only — <c>TechnicalSpecifications</c> was removed
    /// in spec 035, so the value is ignored. Set <paramref name="withImpact"/> to
    /// also pick + fill the per-item impact template (a fully submittable item);
    /// leave it false to add an impact-pending item (the legacy two-step flow sets
    /// impact afterward via <c>SetImpactFromEditAsync</c>).
    /// </summary>
    public async Task AddItemAsync(int appId, string productName, int categoryIndex, string techSpecs, string baseUrl, bool withImpact = false)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/Add");
        await SelectCategoryAndFillFieldsAsync(categoryIndex);
        await ProductNameInput.FillAsync(productName);
        if (withImpact)
        {
            await SelectImpactAndFillAsync();
        }
        await SubmitButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    /// <summary>
    /// Edits a line item. As with Add, <paramref name="techSpecs"/> is ignored
    /// (spec 035). Re-selects the category (which re-loads + clears its fields) and
    /// re-fills them so the post stays valid.
    /// </summary>
    public async Task EditItemAsync(int appId, int itemId, string productName, int categoryIndex, string techSpecs, string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/{itemId}/Edit");
        await SelectCategoryAndFillFieldsAsync(categoryIndex);
        await ProductNameInput.ClearAsync();
        await ProductNameInput.FillAsync(productName);
        await SubmitButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    /// <summary>
    /// Spec 035 — sets the per-item impact on an existing item via its Edit page:
    /// the category fields are server-pre-rendered (already valid), so this only
    /// picks + fills the impact template and saves. Used by the base helper to make
    /// every item of a draft impact-complete (replaces the old app-level impact step).
    /// </summary>
    public async Task SetImpactViaEditAsync(int appId, int itemId, string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/{itemId}/Edit");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await SelectImpactAndFillAsync();
        await SubmitButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));
    }
}
