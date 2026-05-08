using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 016 / Story 4 — admin deletes a group and the cascade is observable
/// end-to-end through the UI: users formerly in only that group still
/// appear in the admin user list and can still log in; users in multiple
/// groups retain the surviving group.
/// </summary>
public class GroupDeletionCascadeTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string TempPwd = "TempPass1!";

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"casc_admin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, Pwd, "Casc", "Admin", $"CSA-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Pwd);
    }

    [Test]
    public async Task DeleteGroup_PreservesUsers_AndOtherGroupMemberships()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);

        // Create two groups.
        var groupA = $"CASC-{unique}-A";
        var groupB = $"CASC-{unique}-B";
        var groupsPage = new AdminGroupsPage(Page);
        await groupsPage.GoToCreateAsync(BaseUrl);
        await groupsPage.CreateGroupAsync(groupA);
        await groupsPage.GoToCreateAsync(BaseUrl);
        await groupsPage.CreateGroupAsync(groupB);

        // Create a reviewer in BOTH groups.
        var dualEmail = $"casc_dual_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Dual", "Member", dualEmail, null, "Reviewer", TempPwd, null);
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync(groupA, groupB);
        await createPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));

        // Create a reviewer in ONLY group A.
        var aOnlyEmail = $"casc_aonly_{unique}@example.com";
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("AOnly", "Member", aOnlyEmail, null, "Reviewer", TempPwd, null);
        await formPage.SelectGroupsAsync(groupA);
        await createPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));

        // Delete group A through the Edit screen.
        await groupsPage.GoToIndexAsync(BaseUrl);
        await groupsPage.RowEditButton(groupA).ClickAsync();
        await groupsPage.DeleteGroupAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Groups(\\?.*)?$"));

        // The dual-membership user must keep group B.
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(dualEmail);
        await listPage.RowEditLink(dualEmail).ClickAsync();
        var formAfter = new AdminUserFormPage(Page);
        var stillSelected = await formAfter.GetSelectedGroupNamesAsync();
        Assert.That(stillSelected, Is.EquivalentTo(new[] { groupB }),
            "FR-004: deleting group A removes the dual user's A membership but leaves B.");

        // The A-only user remains in the system (the user list still shows them).
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(aOnlyEmail);
        await Expect(listPage.RowFor(aOnlyEmail)).ToBeVisibleAsync();
    }
}
