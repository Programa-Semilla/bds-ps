using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

public class RoleAwareSidebarAdminEntriesTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    private async Task RegisterAndLoginAsync(string slug, string? role)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"sidebar_adm_{slug}_{unique}@example.com";
        await RegisterUserAsync(Page, email, Password, "Sidebar", slug, $"SAE-{unique}");
        if (role is not null)
        {
            await AssignRoleAsync(email, role);
        }
        await LoginAsync(Page, email, Password);
    }

    [Test]
    public async Task Admin_Sidebar_ShowsUsersAndReportsEntries()
    {
        await RegisterAndLoginAsync("admin", "Admin");
        await Page.GotoAsync(BaseUrl + "/");

        var basePage = new ApplicationPage(Page);

        // users/reports live under the collapsable Administración group — expand it.
        await basePage.ExpandSidebarSectionAsync("admin-section");
        await Expect(basePage.SidebarEntry("users")).ToBeVisibleAsync();
        await Expect(basePage.SidebarEntry("reports")).ToBeVisibleAsync();

        // Admin also still sees the Reviewer entries (claims transformation), but
        // for admins they move under the collapsable "Operativo" group — expand it.
        await basePage.ExpandSidebarSectionAsync("operativo-section");
        await Expect(basePage.SidebarEntry("review-queue")).ToBeVisibleAsync();
        await Expect(basePage.SidebarEntry("signing-inbox")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Reviewer_Sidebar_DoesNotShow_AdminEntries()
    {
        await RegisterAndLoginAsync("reviewer", "Reviewer");
        await Page.GotoAsync(BaseUrl + "/");

        var basePage = new ApplicationPage(Page);
        Assert.That(await basePage.SidebarEntry("users").CountAsync(), Is.EqualTo(0));
        Assert.That(await basePage.SidebarEntry("reports").CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Applicant_Sidebar_DoesNotShow_AdminEntries()
    {
        await RegisterAndLoginAsync("applicant", role: null);
        await Page.GotoAsync(BaseUrl + "/");

        var basePage = new ApplicationPage(Page);
        Assert.That(await basePage.SidebarEntry("users").CountAsync(), Is.EqualTo(0));
        Assert.That(await basePage.SidebarEntry("reports").CountAsync(), Is.EqualTo(0));
    }
}
