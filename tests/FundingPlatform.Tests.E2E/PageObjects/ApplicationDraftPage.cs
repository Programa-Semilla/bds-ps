// Spec 021 / US2 — POM for the applicant draft editor (/Application/Edit/{id}).
// Covers the autosave indicator, the CompanyName input, the Impact card, the
// inline add-item form, and the gated "Revisar y enviar" submit button.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public sealed class ApplicationDraftPage
{
    private readonly IPage _page;

    public ApplicationDraftPage(IPage page) { _page = page; }

    public ILocator CompanyNameInput => _page.Locator("[data-testid=application-edit-company-name]");
    public ILocator AutosaveIndicator => _page.Locator("[data-autosave-indicator]").First;

    public ILocator ImpactCard => _page.Locator("[data-testid=application-edit-impact-card]");
    public ILocator ImpactStatus => _page.Locator("[data-testid=application-edit-impact-status]");
    public ILocator ImpactLink => _page.Locator("[data-testid=application-edit-impact-link]");

    public ILocator ItemNameInput => _page.Locator("[data-testid=application-edit-item-name]");
    public ILocator ItemCategorySelect => _page.Locator("[data-testid=application-edit-item-category]");
    public ILocator ItemSpecsInput => _page.Locator("[data-testid=application-edit-item-specs]");
    public ILocator AddItemButton => _page.Locator("[data-testid=application-edit-add-item]");
    public ILocator ItemRows => _page.Locator("[data-testid=application-edit-item-row]");

    public ILocator FxDisclaimer => _page.Locator("[data-testid=fx-disclaimer]").First;
    public ILocator SubmitButton => _page.Locator("[data-testid=application-edit-submit]");
    public ILocator StageCountdownBanner => _page.Locator("[data-testid=stage-countdown-banner-slot]");

    public async Task FillCompanyNameAsync(string value)
    {
        await CompanyNameInput.FillAsync(value);
        await CompanyNameInput.BlurAsync();
    }

    /// <summary>
    /// Spec 021 / FR-005 — adds an item via the inline add-item form embedded
    /// in the draft editor. Picks the first real category option. Posts back
    /// to the editor, so the page reloads on the same surface.
    /// </summary>
    public async Task AddItemAsync(string productName, string specifications)
    {
        await ItemNameInput.FillAsync(productName);
        await ItemCategorySelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await ItemSpecsInput.FillAsync(specifications);
        await AddItemButton.ClickAsync();
    }

    /// <summary>Clicks the gated submit button; routes to the /review page.</summary>
    public async Task GoToReviewAsync()
    {
        await SubmitButton.ClickAsync();
    }
}
