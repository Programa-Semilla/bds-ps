using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class AuthenticationTests : AuthenticatedTestBase
{
    // Spec 032 — public self-registration is removed (see RegistrationRemovedTests).
    // An admin-provisioned account (here seeded via the dev-only seam that replaces
    // the old Register POST) must still log in, log out, and log back in.
    [Test]
    public async Task ProvisionedUser_CanLoginLogoutAndLoginAgain()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"test_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Test", "User", $"LID-{uniqueId}");

        // Login
        var loginPage = new LoginPage(Page);
        await loginPage.GotoAsync(BaseUrl);
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
