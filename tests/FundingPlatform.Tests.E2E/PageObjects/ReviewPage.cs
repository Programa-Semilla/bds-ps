// Spec 021 / T088 (spec 035 update) — POM for the /review surface. Encapsulates
// the items card, total, FX disclaimer, and "Confirmar y enviar" button. Spec 035
// removed the app-level impact card (impact renders per line item now).

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public sealed class ReviewPage
{
    private readonly IPage _page;

    public ReviewPage(IPage page) { _page = page; }

    public ILocator PublicCodeBadge => _page.Locator("[data-testid=review-public-code]");
    public ILocator ItemsCard => _page.Locator("[data-testid=review-items-card]");
    public ILocator TotalCrc => _page.Locator("[data-testid=review-total-crc]");
    public ILocator FxDisclaimer => _page.Locator("[data-testid=fx-disclaimer]").First;
    public ILocator ConfirmButton => _page.Locator("[data-testid=review-confirm-submit]");
    public ILocator CannotSubmitNotice => _page.Locator("[data-testid=review-cannot-submit]");

    public async Task ConfirmAsync()
    {
        await ConfirmButton.ClickAsync();
    }
}
