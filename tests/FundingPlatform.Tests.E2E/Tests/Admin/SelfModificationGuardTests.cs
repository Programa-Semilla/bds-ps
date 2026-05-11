using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

public class SelfModificationGuardTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    private async Task<string> SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"selfmod_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Self", "Mod", $"SLM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
        return adminEmail;
    }

    [Test]
    public async Task Admin_OwnRow_DisableButtonHidden()
    {
        var adminEmail = await SignInAsAdminAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(adminEmail);

        // The own-row Disable button is hidden when IsSelf == true.
        await Expect(listPage.RowDisableButton(adminEmail)).Not.ToBeVisibleAsync();
        await Expect(listPage.RowResetPasswordLink(adminEmail)).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_CannotChangeOwnRoleViaEditForm_Rejected()
    {
        var adminEmail = await SignInAsAdminAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(adminEmail);
        await listPage.RowEditLink(adminEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await editPage.SetRoleAsync("Reviewer");
        await editPage.SubmitAsync();

        await Expect(Page.Locator(".validation-summary-errors, .field-validation-error").First).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Admin/Users/[^/]+/Edit"));
    }

    [Test]
    public async Task Admin_CannotChangeOwnEmailViaEditForm_Rejected()
    {
        var adminEmail = await SignInAsAdminAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(adminEmail);
        await listPage.RowEditLink(adminEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await editPage.SetEmailAsync($"new_{adminEmail}");
        await editPage.SubmitAsync();

        await Expect(Page.Locator(".validation-summary-errors, .field-validation-error").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_CanChangeOwnFirstNameLastNamePhone_Allowed()
    {
        var adminEmail = await SignInAsAdminAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(adminEmail);
        await listPage.RowEditLink(adminEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await editPage.FirstName.FillAsync("RenamedFirst");
        await editPage.LastName.FillAsync("RenamedLast");
        await editPage.Phone.FillAsync("+1234567890");
        await editPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Admin/Users(\\?.*)?$"));
    }
}
