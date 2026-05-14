// Spec 021 / T088 — POM for the applicant draft editor surface
// (/Applications/{id}/Edit). Encapsulates the autosave indicator, the
// CompanyName input, the Add-Item button, the FX-disclaimer surface, and
// the "Revisar y enviar" CTA.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public sealed class ApplicationDraftPage
{
    private readonly IPage _page;

    public ApplicationDraftPage(IPage page) { _page = page; }

    public ILocator CompanyNameInput => _page.Locator("[data-testid=application-edit-company-name]");
    public ILocator AddItemButton => _page.Locator("[data-testid=application-edit-add-item]");
    public ILocator FxDisclaimer => _page.Locator("[data-testid=fx-disclaimer]").First;
    public ILocator AutosaveIndicator => _page.Locator("[data-autosave-indicator]").First;
    public ILocator ReviewLink => _page.Locator("[data-testid=application-edit-submit-link]");
    public ILocator StageCountdownBanner => _page.Locator("[data-testid=stage-countdown-banner-slot]");

    public async Task FillCompanyNameAsync(string value)
    {
        await CompanyNameInput.FillAsync(value);
        await CompanyNameInput.BlurAsync();
    }

    public async Task GoToReviewAsync()
    {
        await ReviewLink.ClickAsync();
    }
}
