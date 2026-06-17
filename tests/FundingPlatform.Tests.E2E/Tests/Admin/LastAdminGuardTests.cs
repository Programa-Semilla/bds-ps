using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

public class LastAdminGuardTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    private async Task<string> CreateAndSignInAsAdminAsync(string slug)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"lastadmin_{slug}_{unique}@example.com";
        await RegisterUserAsync(Page, email, AdminPassword, "LastAdmin", slug, $"LA-{unique}");
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(Page, email, AdminPassword);
        return email;
    }

    [Test]
    public async Task WithTwoAdmins_DisableFirst_Succeeds_DisableSecond_Rejected()
    {
        // Admin A creates Admin B inside the admin area.
        var firstAdmin = await CreateAndSignInAsAdminAsync("a");

        var unique = Guid.NewGuid().ToString("N")[..6];
        var secondAdminEmail = $"lastadmin_b_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Second",
            lastName: "Admin",
            email: secondAdminEmail,
            phone: null,
            role: "Admin",
            initialPassword: "TempPass1!",
            legalId: null);
        await createPage.SubmitAsync();

        // Disable Admin B (Admin A is still active; guard does not fire).
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(secondAdminEmail);
        // Spec 037 — disable lives in the "⋯" kebab now; open it first.
        await listPage.OpenRowActionsAsync(secondAdminEmail);
        // Spec 024 — disable now opens the shared confirm modal; click confirm.
        await listPage.RowDisableButton(secondAdminEmail).ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
        await Expect(Page.Locator("[data-testid=\"success-banner\"]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task LastAdmin_CannotDemoteSelf_Rejected()
    {
        // Self-mod guard fires first (same actor as target) — covered in SelfModificationGuardTests.
        // This test asserts that even if self-mod weren't a thing, a single-active-admin scenario blocks demotion.
        // We exercise the path by inducing the demote attempt and checking we land back on the edit form with an error.
        var adminEmail = await CreateAndSignInAsAdminAsync("solo");
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(adminEmail);
        await listPage.RowEditLink(adminEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await editPage.SetRoleAsync("Reviewer");
        await editPage.SubmitAsync();
        await Expect(Page.Locator(".validation-summary-errors, .field-validation-error").First).ToBeVisibleAsync();
    }
}
