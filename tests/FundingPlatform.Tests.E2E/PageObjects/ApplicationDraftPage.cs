// Spec 021 / US2 (spec 035 update) — POM for the applicant draft editor
// (/Application/Edit/{id}). Spec 035 removed the application-level Impact card and
// the inline add-item form: impact is now captured per line item on the item form,
// and "Agregar línea" links to ItemController.Add. This POM keeps the company-name
// autosave surface, the items table, and the gated "Revisar y enviar" button.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public sealed class ApplicationDraftPage
{
    private readonly IPage _page;

    public ApplicationDraftPage(IPage page) { _page = page; }

    public ILocator CompanyNameInput => _page.Locator("[data-testid=application-edit-company-name]");
    public ILocator AutosaveIndicator => _page.Locator("[data-autosave-indicator]").First;

    /// <summary>The "Agregar línea" link — routes to the category-first item form.</summary>
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

    /// <summary>Clicks the gated submit button; routes to the /review page.</summary>
    public async Task GoToReviewAsync()
    {
        await SubmitButton.ClickAsync();
    }
}
