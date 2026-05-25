using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 016 / Story 2 — admin assigns one or more groups to non-admin users.
/// Drives the real user journey through the admin UI; covers FR-007..FR-010.
/// </summary>
public class AdminUserGroupAssignmentTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string TempUserPassword = "TempPass1!";

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"groupasn_admin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Admin", "Group", $"GAS-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    /// <summary>
    /// Spec 021 / FR-001 — Groups are created from the Process detail page.
    /// Creates one Active Process and the given Groups under it.
    /// </summary>
    private async Task CreateGroupsUnderNewProcessAsync(string suffix, params string[] groupNames)
    {
        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync($"GASProc-{suffix}");
        var processId = await procPage.OpenProcessDetailByNameAsync(BaseUrl, $"GASProc-{suffix}");
        foreach (var name in groupNames)
        {
            await procPage.GoToDetailsAsync(BaseUrl, processId);
            await procPage.CreateGroupAsync(name);
        }
    }

    /// <summary>
    /// Story 2 acceptance scenario 1 — Reviewer + zero groups → validation error,
    /// no user created.
    /// </summary>
    [Test]
    public async Task Create_Reviewer_WithZeroGroups_IsBlocked()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var newReviewerEmail = $"zero_groups_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Zero",
            lastName: "Groups",
            email: newReviewerEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        // FillAsync auto-fills the multi-select for non-Admin roles to keep
        // pre-016 tests green; this test specifically asserts the zero-groups
        // blocker, so re-clear the selection.
        var formPage = new AdminUserFormPage(Page);
        await formPage.ClearGroupSelectionAsync();
        await createPage.SubmitAsync();

        await Expect(formPage.GroupsError).ToBeVisibleAsync();
        // Form should still be on the Create page, not redirected to Index.
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users/Create"));
    }

    /// <summary>
    /// Story 2 acceptance scenario 2 — Applicant + ≥1 group → success.
    /// </summary>
    [Test]
    public async Task Create_Applicant_WithGroups_Succeeds()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var applicantEmail = $"asn_app_{unique}@example.com";

        // Pre-create a couple of groups so we have known names to select.
        await CreateGroupsUnderNewProcessAsync(unique, $"AS-{unique}-A", $"AS-{unique}-B");

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "App",
            lastName: "Sub",
            email: applicantEmail,
            phone: null,
            role: "Applicant",
            initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"LAS-{unique}"));

        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync($"AS-{unique}-A", $"AS-{unique}-B");
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));
        var listPage = new AdminUsersListPage(Page);
        await listPage.SearchAsync(applicantEmail);
        await Expect(listPage.RowFor(applicantEmail)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Story 2 acceptance scenario 4 — promoting a Reviewer to Admin discards
    /// memberships (server silently clears them on save).
    /// </summary>
    [Test]
    public async Task Promote_Reviewer_ToAdmin_ClearsMemberships()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var reviewerEmail = $"promo_{unique}@example.com";

        await CreateGroupsUnderNewProcessAsync(unique, $"PR-{unique}-A");

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Pro",
            lastName: "Mote",
            email: reviewerEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync($"PR-{unique}-A");
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));
        var listPage = new AdminUsersListPage(Page);
        await listPage.SearchAsync(reviewerEmail);
        await listPage.RowEditLink(reviewerEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await editPage.SetRoleAsync("Admin");
        await editPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));

        // Re-open: groups field is hidden, no groups selected.
        await listPage.SearchAsync(reviewerEmail);
        await listPage.RowEditLink(reviewerEmail).ClickAsync();
        var formPage2 = new AdminUserFormPage(Page);
        // Wait for the inline JS toggle to run; it sets display:none on
        // groupsField when role=Admin. IsVisibleAsync auto-waits for the
        // element to settle.
        await Expect(formPage2.GroupsField).ToBeHiddenAsync();
    }

    /// <summary>
    /// Story 2 acceptance scenario 5 — demoting an Admin to Reviewer with no
    /// groups selected is blocked at submit time.
    /// </summary>
    [Test]
    public async Task Demote_Admin_ToReviewer_WithNoGroups_IsBlocked()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var adminEmail = $"demo_{unique}@example.com";

        await CreateGroupsUnderNewProcessAsync(unique, $"DM-{unique}-X");

        // Need at least 2 admins for the demotion not to hit the
        // last-admin-protection guard. The signed-in admin is the first; create
        // a second standalone admin here.
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Sec",
            lastName: "Admin",
            email: adminEmail,
            phone: null,
            role: "Admin",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));

        var listPage = new AdminUsersListPage(Page);
        await listPage.SearchAsync(adminEmail);
        await listPage.RowEditLink(adminEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await editPage.SetRoleAsync("Reviewer");
        // Do NOT select any group.
        await editPage.SubmitAsync();

        var formPage = new AdminUserFormPage(Page);
        await Expect(formPage.GroupsError).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users/.+/Edit"));
    }
}
