// Spec 021 / US5 / T125 — Page Object for the /Account/ForgotPassword surface.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ForgotPasswordPage : BasePage
{
    public ForgotPasswordPage(IPage page) : base(page)
    {
    }

    public ILocator EmailInput => Page.Locator("[data-testid=\"forgot-password-email\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"forgot-password-submit\"]");
    public ILocator SuccessBanner => Page.Locator("[data-testid=\"success-banner\"]");

    public async Task GotoAsync(string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Account/ForgotPassword");
    }

    public async Task SubmitAsync(string email)
    {
        await EmailInput.FillAsync(email);
        await SubmitButton.ClickAsync();
    }
}
