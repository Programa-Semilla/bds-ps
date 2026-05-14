using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

public class SentinelExclusionTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string SentinelEmail = "admin@programa-semilla.test";

    private async Task SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"sentinel_excl_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Sentinel", "Excl", $"SEX-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [Test]
    public async Task Sentinel_NotInUsersList_NoFilter()
    {
        await SignInAsAdminAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await Expect(listPage.RowFor(SentinelEmail)).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Sentinel_NotInUsersList_RoleFilterAdmin()
    {
        await SignInAsAdminAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.FilterByRoleAsync("Admin");
        await Expect(listPage.RowFor(SentinelEmail)).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Sentinel_NotInUsersList_SearchByEmailFragment()
    {
        await SignInAsAdminAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync("FundingPlatform");
        await Expect(listPage.RowFor(SentinelEmail)).Not.ToBeVisibleAsync();
    }
}
