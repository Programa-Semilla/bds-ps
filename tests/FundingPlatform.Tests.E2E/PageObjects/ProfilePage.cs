// Spec 021 / US5 / T125 — Page Object for the /Profile self-service surface.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class ProfilePage : BasePage
{
    public ProfilePage(IPage page) : base(page)
    {
    }

    public ILocator EditCard => Page.Locator("[data-testid=\"profile-edit-card\"]");
    public ILocator PasswordCard => Page.Locator("[data-testid=\"profile-password-card\"]");

    // Self-editable inputs.
    public ILocator FirstNameInput => Page.Locator("[data-testid=\"profile-firstname\"]");
    public ILocator LastNameInput => Page.Locator("[data-testid=\"profile-lastname\"]");
    public ILocator PhoneInput => Page.Locator("[data-testid=\"profile-phone\"]");
    public ILocator AddressInput => Page.Locator("[data-testid=\"profile-address\"]");
    public ILocator SaveButton => Page.Locator("[data-testid=\"profile-save\"]");

    // Read-only "administrado" fields.
    public ILocator EmailField => Page.Locator("[data-testid=\"profile-email\"]");
    public ILocator RoleField => Page.Locator("[data-testid=\"profile-role\"]");
    public ILocator GroupField => Page.Locator("[data-testid=\"profile-group\"]");
    public ILocator CodigoPersonalField => Page.Locator("[data-testid=\"profile-codigopersonal\"]");
    public ILocator AdministradoBadges => Page.Locator("[data-testid=\"administrado-badge\"]");

    // Password panel.
    public ILocator OldPasswordInput => Page.Locator("[data-testid=\"profile-oldpassword\"]");
    public ILocator NewPasswordInput => Page.Locator("[data-testid=\"profile-newpassword\"]");
    public ILocator ConfirmPasswordInput => Page.Locator("[data-testid=\"profile-confirmpassword\"]");
    public ILocator ChangePasswordSubmit => Page.Locator("[data-testid=\"profile-change-password-submit\"]");

    public async Task GotoAsync(string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Profile");
    }

    public async Task EditAndSaveAsync(string firstName, string lastName, string phone, string address)
    {
        await FirstNameInput.FillAsync(firstName);
        await LastNameInput.FillAsync(lastName);
        await PhoneInput.FillAsync(phone);
        await AddressInput.FillAsync(address);
        await SaveButton.ClickAsync();
    }
}
