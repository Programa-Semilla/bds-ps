using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class AuthenticationTests : AuthenticatedTestBase
{
    [Test]
    public async Task Register_And_Login_Successfully()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"test_{uniqueId}@example.com";
        var password = "Test123!";
        var firstName = "Test";
        var lastName = "User";
        var legalId = IdentificationData.CedulaFisica($"LID-{uniqueId}");

        // Register
        var registerPage = new RegisterPage(Page);
        await registerPage.GotoAsync(BaseUrl);
        await registerPage.RegisterAsync(email, password, firstName, lastName, legalId);

        // Should redirect to login page after registration
        await Expect(Page).ToHaveURLAsync(new Regex("/Account/Login"));

        // Login
        var loginPage = new LoginPage(Page);
        await loginPage.LoginAsync(email, password);

        // Should redirect to home page after login
        await Expect(Page).ToHaveURLAsync(new Regex("/$"));

        // Logout
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Login again to verify
        var loginPage2 = new LoginPage(Page);
        await loginPage2.GotoAsync(BaseUrl);
        await loginPage2.LoginAsync(email, password);

        await Expect(Page).ToHaveURLAsync(new Regex("/$"));
    }

    [Test]
    public async Task Login_WithInvalidCredentials_ShowsError()
    {
        var loginPage = new LoginPage(Page);
        await loginPage.GotoAsync(BaseUrl);
        await loginPage.LoginAsync("nonexistent@example.com", "WrongPassword1!");

        // Should stay on login page
        await Expect(Page).ToHaveURLAsync(new Regex("/Account/Login"));

        // Should show error message in validation summary
        var validationSummary = Page.Locator("[data-valmsg-summary] li, .validation-summary-errors li");
        await Expect(validationSummary.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProtectedPage_RedirectsToLogin()
    {
        // Attempt to access a protected page without authentication
        await Page.GotoAsync($"{BaseUrl}/Application");

        // Should redirect to login page
        await Expect(Page).ToHaveURLAsync(new Regex("/Account/Login"));
    }
}
