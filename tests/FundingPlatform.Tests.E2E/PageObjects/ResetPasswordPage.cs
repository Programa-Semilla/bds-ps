// Spec 021 / US5 / T125 — Page Object for the /Account/ResetPassword surface.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ResetPasswordPage : BasePage
{
    public ResetPasswordPage(IPage page) : base(page)
    {
    }

    public ILocator FormRoot => Page.Locator("[data-testid=\"reset-password-form\"]");
    public ILocator InvalidLinkMessage => Page.Locator("[data-testid=\"reset-password-invalid\"]");
    public ILocator NewPasswordInput => Page.Locator("[data-testid=\"reset-password-new\"]");
    public ILocator ConfirmPasswordInput => Page.Locator("[data-testid=\"reset-password-confirm\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"reset-password-submit\"]");
    public ILocator StrengthLegend => Page.Locator("[data-testid=\"password-strength-legend\"]");
    public ILocator ValidationSummary => Page.Locator("[data-testid=\"reset-password-summary\"]");

    /// <summary>Returns the legend's ok-state for the given rule (min8 / upper / digit / special).</summary>
    public ILocator LegendRule(string rule) =>
        StrengthLegend.Locator($"li[data-rule=\"{rule}\"]");

    public async Task SubmitAsync(string newPassword, string confirmPassword)
    {
        await NewPasswordInput.FillAsync(newPassword);
        await ConfirmPasswordInput.FillAsync(confirmPassword);
        await SubmitButton.ClickAsync();
    }
}
