// Spec 021 / US5 / T124 / FR-018 / FR-026 / FR-027 / FR-028 — E2E coverage
// for the profile + forgot-password user story.
//
// Three flows in one test fixture:
//   1. Forgot-password full path: /Account/ForgotPassword → email captured
//      via dev-only helper → open reset link → strength legend ticks live →
//      set new password → land on /Account/Login → log in successfully.
//   2. Reuse-token path: open the same reset link a second time →
//      "Enlace inválido o expirado".
//   3. Profile-edit path: navigate to /Profile → edit the four self-fields →
//      save succeeds; Email / Role / Group / CodigoPersonal render as
//      read-only with "administrado" badge.

using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

[TestFixture]
public class US5_ProfileAndForgotPassword : AuthenticatedTestBase
{
    [Test]
    public async Task ForgotPassword_FullLoop_ResetsAndLogsIn()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string oldPassword = "Test123!";
        const string newPassword = "NewPass1!";
        var applicantEmail = $"us5_app_{uniqueId}@example.com";

        await RegisterUserAsync(Page, applicantEmail, oldPassword, "Vivi", "Olvido", $"VAPP-{uniqueId}");

        // ----- 1. Land on the login page, click the "¿Olvidó su contraseña?" link.
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.Locator("[data-testid=\"forgot-password-link\"]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/Account/ForgotPassword$"));

        // ----- 2. Submit the forgot-password form.
        var forgot = new ForgotPasswordPage(Page);
        await forgot.SubmitAsync(applicantEmail);
        await Expect(forgot.SuccessBanner).ToBeVisibleAsync();

        // ----- 3. Capture the reset link via the dev-only helper (production
        //          would send this in an email; the LoggingEmailSender dev
        //          fallback only writes to the log).
        var resetLink = await GetLatestPasswordResetLinkAsync(applicantEmail);
        Assert.That(resetLink, Is.Not.Null.And.Not.Empty);
        Assert.That(resetLink, Does.Contain("/Account/ResetPassword"));

        // ----- 4. Open the reset link and verify the strength legend ticks
        //          live as the user types.
        await Page.GotoAsync(resetLink!);
        var reset = new ResetPasswordPage(Page);
        await Expect(reset.FormRoot).ToBeVisibleAsync();
        await Expect(reset.StrengthLegend).ToBeVisibleAsync();

        // Type a partial password and assert at least one rule ticks.
        await reset.NewPasswordInput.FillAsync("a");
        // No rules met yet on the lone "a".
        await Expect(reset.LegendRule("min8")).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bok\b"));
        // Now fill the full strong password — all four rules should tick.
        await reset.NewPasswordInput.FillAsync(newPassword);
        await Expect(reset.LegendRule("min8")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bok\b"));
        await Expect(reset.LegendRule("upper")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bok\b"));
        await Expect(reset.LegendRule("digit")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bok\b"));
        await Expect(reset.LegendRule("special")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bok\b"));

        // ----- 5. Submit the new password. We land on the login page.
        await reset.ConfirmPasswordInput.FillAsync(newPassword);
        await reset.SubmitButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/Account/Login"));

        // ----- 6. Log in with the new password.
        await LoginAsync(Page, applicantEmail, newPassword);
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/"));

        // ----- 7. Reuse-token path — re-open the same reset link. The
        //          single-use marker MUST reject the second attempt.
        await Page.GotoAsync(resetLink!);
        await reset.NewPasswordInput.FillAsync(newPassword);
        await reset.ConfirmPasswordInput.FillAsync(newPassword);
        await reset.SubmitButton.ClickAsync();
        // The controller renders the form with the validation summary set to
        // the spec-aligned "Enlace inválido o expirado" copy.
        await Expect(reset.ValidationSummary).ToContainTextAsync("Enlace inválido o expirado");
    }

    [Test]
    public async Task Profile_EditsSelfFields_AndReadOnlyAdminFieldsBlocked()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"us5_profile_{uniqueId}@example.com";

        await RegisterUserAsync(Page, applicantEmail, password, "InitialFirst", "InitialLast", $"VAPP-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var profile = new ProfilePage(Page);
        await profile.GotoAsync(BaseUrl);
        await Expect(profile.EditCard).ToBeVisibleAsync();

        // ----- Read-only fields render an "administrado" badge.
        var badgeCount = await profile.AdministradoBadges.CountAsync();
        Assert.That(badgeCount, Is.GreaterThanOrEqualTo(4),
            "Email / Role / Group / CodigoPersonal MUST each render an administrado badge");

        // Email / Role / Group / CodigoPersonal inputs MUST be disabled.
        await Expect(profile.EmailField).ToBeDisabledAsync();
        await Expect(profile.RoleField).ToBeDisabledAsync();
        await Expect(profile.GroupField).ToBeDisabledAsync();
        await Expect(profile.CodigoPersonalField).ToBeDisabledAsync();

        // ----- Edit the four self-fields and save.
        // Spec 026 — the phone field is now CR-masked (8888-8888), so use a CR number.
        await profile.EditAndSaveAsync("Vivi", "Editada", "8000-0000", "San José, Costa Rica");

        // After the POST, the page redirects back to /Profile with a success banner.
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/Profile$"));
        await Expect(Page.Locator("[data-testid=\"success-banner\"]")).ToBeVisibleAsync();

        // Verify the input values came back as the edited ones.
        await Expect(profile.FirstNameInput).ToHaveValueAsync("Vivi");
        await Expect(profile.LastNameInput).ToHaveValueAsync("Editada");
        await Expect(profile.PhoneInput).ToHaveValueAsync("8000-0000");
        await Expect(profile.AddressInput).ToHaveValueAsync("San José, Costa Rica");
    }

    /// <summary>
    /// Dev-only helper — calls /Account/LatestPasswordResetLink which mints a
    /// fresh token + marker row and returns the absolute reset link. The
    /// production path dispatches this via email; the E2E suite reads it
    /// directly so the test can follow the link like a real user would.
    /// </summary>
    private async Task<string?> GetLatestPasswordResetLinkAsync(string email)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var response = await client.GetAsync(
            $"/Account/LatestPasswordResetLink?email={Uri.EscapeDataString(email)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
