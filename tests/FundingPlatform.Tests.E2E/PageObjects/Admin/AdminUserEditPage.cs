using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

public class AdminUserEditPage : AdminBasePage
{
    public AdminUserEditPage(IPage page) : base(page)
    {
    }

    public ILocator FirstName => Page.Locator("input[name=\"FirstName\"]");
    public ILocator LastName => Page.Locator("input[name=\"LastName\"]");
    public ILocator Email => Page.Locator("input[name=\"Email\"]");
    public ILocator Phone => Page.Locator("input[name=\"Phone\"]");
    public ILocator Role => Page.Locator("select[name=\"Role\"]");
    // Spec 026 — identification type selector + masked value input.
    public ILocator IdentificationTypeSelect => Page.Locator("select[name=\"IdentificationType\"]");
    public ILocator LegalId => Page.Locator("input[name=\"LegalId\"]");
    public ILocator LegalIdField => Page.Locator("[data-testid=\"legalid-field\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"admin-user-edit-submit\"]");
    public ILocator ResetPasswordLink => Page.Locator("[data-testid=\"admin-user-edit-reset-password-link\"]");
    public ILocator ValidationSummary => Page.Locator(".validation-summary-errors, .field-validation-error");

    public Task GoToAsync(string baseUrl, string userId) =>
        Page.GotoAsync($"{baseUrl}/Admin/Users/{userId}/Edit");

    public async Task SetEmailAsync(string email)
    {
        await Email.FillAsync(email);
    }

    public async Task SetRoleAsync(string role)
    {
        await Role.SelectOptionAsync(role);
    }

    /// <summary>Spec 026 — returns the currently-selected identification type (enum member name).</summary>
    public Task<string> GetSelectedIdentificationTypeAsync() =>
        IdentificationTypeSelect.InputValueAsync();

    /// <summary>Spec 026 — returns the masked identification value currently in the input.</summary>
    public Task<string> GetLegalIdValueAsync() => LegalId.InputValueAsync();

    /// <summary>Spec 026 — selects the identification type then fills the masked value.</summary>
    public async Task SetIdentificationAsync(string identificationType, string legalId)
    {
        await IdentificationTypeSelect.SelectOptionAsync(identificationType);
        await LegalId.FillAsync(legalId);
    }

    public Task SubmitAsync() => SubmitButton.ClickAsync();
}
