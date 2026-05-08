using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 016 / Story 1 — admin manages the catalog of groups (FR-001..FR-003,
/// FR-006). Drives the real user journey through the admin UI; no deep-link
/// shortcuts to MVC routes the UI never exposes.
/// </summary>
public class AdminGroupCrudTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"groupadmin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Group", "Admin", $"GADM-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [Test]
    public async Task Admin_CreatesGroup_AppearsInListWithMemberCountZero()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var groupName = $"GA-{unique}";

        var page = new AdminGroupsPage(Page);
        await page.GoToIndexAsync(BaseUrl);
        await page.GoToCreateAsync(BaseUrl);
        await page.CreateGroupAsync(groupName);

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Groups(\\?.*)?$"));
        await Expect(page.RowFor(groupName)).ToBeVisibleAsync();
        await Expect(page.RowMemberCount(groupName)).ToHaveTextAsync("0");
    }

    [Test]
    public async Task Admin_CreatesGroup_DuplicateName_ShowsValidationError()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var groupName = $"GB-{unique}";

        var page = new AdminGroupsPage(Page);
        await page.GoToCreateAsync(BaseUrl);
        await page.CreateGroupAsync(groupName);

        // Second creation with the same name (case- and accent-insensitive
        // match per FR-001) — the form re-renders with an inline validation
        // error and no second row appears.
        await page.GoToCreateAsync(BaseUrl);
        await page.CreateGroupAsync(groupName.ToLowerInvariant());

        await Expect(page.NameError).ToContainTextAsync(new Regex("[Yy]a existe"));
        // The form is still on Create — the redirect to Index is the success indicator.
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Groups/Create"));
    }

    [Test]
    public async Task Admin_RenamesGroup_RowReflectsNewName()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var original = $"GC-{unique}";
        var renamed = $"GC2-{unique}";

        var page = new AdminGroupsPage(Page);
        await page.GoToCreateAsync(BaseUrl);
        await page.CreateGroupAsync(original);

        await page.RowEditButton(original).ClickAsync();
        await page.RenameGroupAsync(renamed);

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Groups(\\?.*)?$"));
        await Expect(page.RowFor(renamed)).ToBeVisibleAsync();
    }

    [Test]
    public async Task NonAdmin_DirectAccessToGroupsIndex_Returns403()
    {
        // A reviewer (non-admin) signing in and navigating directly to
        // /Admin/Groups MUST receive 403 (FR-002).
        var unique = Guid.NewGuid().ToString("N")[..6];
        var reviewerEmail = $"groupreviewer_{unique}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, AdminPassword, "Rev", "Iewer", $"GRV-{unique}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, AdminPassword);

        var response = await Page.GotoAsync($"{BaseUrl}/Admin/Groups");
        // ASP.NET Core responds with 403 for an authenticated non-Admin caller.
        // The response object exposes the status; if the framework redirects
        // to /Account/AccessDenied, the redirected page itself is served as
        // 200 — assert on the URL in that case.
        var status = response?.Status ?? 0;
        var ok403 = status == 403
            || (Page.Url.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase));
        Assert.That(ok403, Is.True,
            $"Expected 403 or AccessDenied redirect for non-admin caller. Status={status}, Url={Page.Url}");
    }
}
