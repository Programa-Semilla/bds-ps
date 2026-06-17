using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Regression coverage for the admin password-reset surface
/// (<c>/Admin/Users/{id}/ResetPassword</c>).
///
/// Bug: a new password that passed the view-model length check but failed the
/// ASP.NET Identity complexity policy produced a <c>WEAK_PASSWORD</c> error
/// keyed to the field name "InitialPassword" — a field that exists only on the
/// Create-user form. The reset form rendered that key nowhere, so the admin
/// saw no error and no redirect: the click appeared to do nothing. Worse, the
/// service had already removed the old password before the failed add, leaving
/// the target user with no password at all.
/// </summary>
public class AdminResetPasswordTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string TargetOriginalPassword = "Original1!";

    private async Task<(string adminEmail, string targetEmail)> SeedAdminAndTargetAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];

        var targetEmail = $"reset_target_{unique}@example.com";
        await RegisterUserAsync(Page, targetEmail, TargetOriginalPassword, "Reset", "Target", $"RT-{unique}");

        var adminEmail = $"reset_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Reset", "Admin", $"RA-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);

        return (adminEmail, targetEmail);
    }

    private async Task OpenResetPageAsync(string targetEmail)
    {
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(targetEmail);
        await listPage.OpenRowActionsAsync(targetEmail);
        await listPage.RowResetPasswordLink(targetEmail).ClickAsync();
    }

    [Test]
    public async Task ResetPassword_PolicyRejectedPassword_ShowsErrorAndKeepsOldPasswordWorking()
    {
        var (_, targetEmail) = await SeedAdminAndTargetAsync();
        await OpenResetPageAsync(targetEmail);

        // Spec 024 — SubmitAsync clicks the shared confirm modal's confirm button.
        // "abcdef" — 6 chars, so it clears the view-model length rule, but it
        // has no uppercase / digit / symbol and is rejected by the Identity
        // complexity policy. This is the exact path that used to fail silently.
        var resetPage = new AdminResetPasswordPage(Page);
        await resetPage.SubmitAsync("abcdef", "abcdef");

        // The rejection must be visible to the admin, not swallowed.
        await Expect(resetPage.ValidationSummary).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users/[^/]+/ResetPassword"));

        // The failed reset must NOT have wiped the target's existing password —
        // the original credentials must still authenticate.
        await LoginAsync(Page, targetEmail, TargetOriginalPassword);
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/Login"));
    }

    [Test]
    public async Task ResetPassword_ValidPassword_RedirectsWithSuccessBanner()
    {
        var (_, targetEmail) = await SeedAdminAndTargetAsync();
        await OpenResetPageAsync(targetEmail);

        var resetPage = new AdminResetPasswordPage(Page);
        await resetPage.SubmitAsync("Temp9Pass!", "Temp9Pass!");

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));
        await Expect(Page.Locator("[data-testid=\"success-banner\"]")).ToBeVisibleAsync();
    }
}
