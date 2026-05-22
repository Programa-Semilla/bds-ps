using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Page Object for the admin-driven <c>/Admin/Users/{id}/ResetPassword</c>
/// surface (distinct from the public <c>/Account/ResetPassword</c> token flow).
/// </summary>
public class AdminResetPasswordPage : AdminBasePage
{
    public AdminResetPasswordPage(IPage page) : base(page)
    {
    }

    public ILocator FormRoot => Page.Locator("[data-testid=\"admin-user-reset-form\"]");
    public ILocator NewPasswordInput => Page.Locator("input[name=\"NewTemporaryPassword\"]");
    public ILocator ConfirmPasswordInput => Page.Locator("input[name=\"ConfirmPassword\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"admin-user-reset-submit\"]");
    public ILocator ValidationSummary => Page.Locator(".validation-summary-errors");

    /// <summary>
    /// Fills both fields and submits. Spec 024 — the submit button opens the shared
    /// confirm modal; this method clicks the modal's confirm button to proceed.
    /// </summary>
    public async Task SubmitAsync(string newPassword, string confirmPassword)
    {
        await NewPasswordInput.FillAsync(newPassword);
        await ConfirmPasswordInput.FillAsync(confirmPassword);
        await SubmitButton.ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
    }
}
