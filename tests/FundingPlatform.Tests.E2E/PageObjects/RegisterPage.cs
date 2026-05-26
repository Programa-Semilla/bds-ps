using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class RegisterPage : BasePage
{
    public RegisterPage(IPage page) : base(page)
    {
    }

    public ILocator AuthShell => Page.Locator("[data-testid=\"auth-shell\"]");

    public async Task<bool> IsAuthShellVisibleAsync()
    {
        if (await Sidebar.CountAsync() > 0) return false;
        return await AuthShell.CountAsync() > 0;
    }

    public ILocator EmailInput => Page.Locator("[name=Email]");
    public ILocator PasswordInput => Page.Locator("[name=Password]");
    public ILocator ConfirmPasswordInput => Page.Locator("[name=ConfirmPassword]");
    public ILocator FirstNameInput => Page.Locator("[name=FirstName]");
    public ILocator LastNameInput => Page.Locator("[name=LastName]");
    // Spec 026 — identification type selector + masked value input.
    public ILocator IdentificationTypeSelect => Page.Locator("[name=IdentificationType]");
    public ILocator LegalIdInput => Page.Locator("[name=LegalId]");
    public ILocator SubmitButton => Page.Locator("main button[type=submit]");

    public async Task GotoAsync(string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Account/Register");
    }

    /// <summary>
    /// Spec 026 — <paramref name="identificationType"/> is the enum member name
    /// (CedulaFisica / Dimex / Nite / Pasaporte). <paramref name="legalId"/> must be
    /// a valid canonical value for that type.
    /// </summary>
    public async Task RegisterAsync(
        string email, string password, string firstName, string lastName, string legalId,
        string identificationType = "CedulaFisica")
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await ConfirmPasswordInput.FillAsync(password);
        await FirstNameInput.FillAsync(firstName);
        await LastNameInput.FillAsync(lastName);
        await IdentificationTypeSelect.SelectOptionAsync(identificationType);
        await LegalIdInput.FillAsync(legalId);
        await SubmitButton.ClickAsync();
    }
}
