using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ChangePasswordPage : BasePage
{
    public ChangePasswordPage(IPage page) : base(page)
    {
    }

    public ILocator OldPassword => Page.Locator("input[name=\"OldPassword\"]");
    public ILocator NewPassword => Page.Locator("input[name=\"NewPassword\"]");
    public ILocator ConfirmPassword => Page.Locator("input[name=\"ConfirmPassword\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"change-password-submit\"]");
    public ILocator ValidationSummary => Page.Locator(".validation-summary-errors, .field-validation-error");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Account/ChangePassword");

    public async Task SubmitAsync(string oldPassword, string newPassword, string confirmPassword)
    {
        await OldPassword.FillAsync(oldPassword);
        await NewPassword.FillAsync(newPassword);
        await ConfirmPassword.FillAsync(confirmPassword);
        await SubmitButton.ClickAsync();
    }
}
