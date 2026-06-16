using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 035 (evolved 2026-06-16) / US3 — POM for the category-first line-item form
/// (<c>/Application/{appId}/Item/Add</c> + <c>/{itemId}/Edit</c>). Selecting a category
/// AJAX-loads its dynamic field set. Impact is no longer picked here: the line item is
/// attributed to the application's declared impacts (checkboxes) and carries a short
/// justification. <c>Item.TechnicalSpecifications</c> is gone.
/// </summary>
public class ItemPage : BasePage
{
    public ItemPage(IPage page) : base(page)
    {
    }

    public ILocator ProductNameInput => Page.Locator("[name=ProductName]");
    public ILocator CategorySelect => Page.Locator("#item-category-select");
    public ILocator CategoryFieldsContainer => Page.Locator("#category-fields");
    public ILocator SubmitButton => Page.Locator("[data-testid=item-save]");
    public ILocator ImpactAttributionOptions => Page.Locator("[data-testid=item-impact-option]");
    public ILocator ImpactJustification => Page.Locator("[data-testid=item-impact-justification]");
    public ILocator NoDeclaredImpactsAlert => Page.Locator("[data-testid=item-no-declared-impacts]");
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
    /// Spec 035 (evolved) — checks the first impact-attribution checkbox and fills the
    /// justification. No-op (returns false) when the application has not declared any
    /// impact yet (the empty-state is shown instead of the checkboxes).
    /// </summary>
    public async Task<bool> AttributeFirstImpactAndJustifyAsync(string justification = "Aporta al impacto declarado.")
    {
        var count = await ImpactAttributionOptions.CountAsync();
        if (count == 0)
        {
            return false;
        }
        await ImpactAttributionOptions.First.CheckAsync();
        await ImpactJustification.FillAsync(justification);
        return true;
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
    /// Adds a line item. <paramref name="techSpecs"/> is retained for call-site
    /// compatibility only (removed in spec 035). Set <paramref name="withImpact"/> to also
    /// attribute the line to the first declared impact + justify (requires the application
    /// to have declared an impact already); leave it false to add an attribution-pending
    /// item (the base helper attributes afterward via <c>SetImpactViaEditAsync</c>).
    /// </summary>
    public async Task AddItemAsync(int appId, string productName, int categoryIndex, string techSpecs, string baseUrl, bool withImpact = false)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/Add");
        await SelectCategoryAndFillFieldsAsync(categoryIndex);
        await ProductNameInput.FillAsync(productName);
        if (withImpact)
        {
            await AttributeFirstImpactAndJustifyAsync();
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
        await AttributeFirstImpactAndJustifyAsync();
        await SubmitButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    /// <summary>
    /// Spec 035 (evolved) — attributes an existing item to the first declared impact +
    /// justifies via its Edit page (the category fields are server-pre-rendered, already
    /// valid). Used by the base helper to make every item of a draft impact-complete.
    /// Requires the application to have declared an impact first.
    /// </summary>
    public async Task SetImpactViaEditAsync(int appId, int itemId, string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/{itemId}/Edit");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await AttributeFirstImpactAndJustifyAsync();
        await SubmitButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));
    }
}
