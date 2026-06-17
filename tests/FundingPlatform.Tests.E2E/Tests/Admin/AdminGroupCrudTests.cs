using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 016 / Story 1 — admin manages the catalog of groups (FR-001..FR-003,
/// FR-006). Spec 021 / FR-001 — Groups are now created from the Process detail
/// page (the owning Process is implied by route context); the catalog index
/// keeps rename / reparent / delete. Drives the real user journey through the
/// admin UI; no deep-link shortcuts to MVC routes the UI never exposes.
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

    /// <summary>Creates an Active Process and opens its detail page. Returns the
    /// Process name so the caller can assert the Groups-index Process column.</summary>
    private async Task<string> CreateProcessAndOpenAsync(ProcessAdminPage procPage, string suffix)
    {
        var processName = $"Proc-{suffix}";
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(processName);
        await procPage.OpenProcessDetailByNameAsync(BaseUrl, processName);
        return processName;
    }

    [Test]
    public async Task Admin_CreatesGroup_AppearsInListWithMemberCountZero()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var groupName = $"GA-{unique}";

        var procPage = new ProcessAdminPage(Page);
        var processName = await CreateProcessAndOpenAsync(procPage, unique);
        await procPage.CreateGroupAsync(groupName);
        await Expect(procPage.GroupRow(groupName)).ToBeVisibleAsync();

        // The group surfaces on the standalone catalog with its owning Process.
        var page = new AdminGroupsPage(Page);
        await page.GoToIndexAsync(BaseUrl);
        await Expect(page.RowFor(groupName)).ToBeVisibleAsync();
        await Expect(page.RowMemberCount(groupName)).ToHaveTextAsync("0");
        await Expect(page.RowProcess(groupName)).ToHaveTextAsync(processName);
    }

    [Test]
    public async Task Admin_CreatesGroup_DuplicateName_ShowsErrorOnProcessDetail()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var groupName = $"GB-{unique}";

        var procPage = new ProcessAdminPage(Page);
        await CreateProcessAndOpenAsync(procPage, unique);
        await procPage.CreateGroupAsync(groupName);
        await Expect(procPage.GroupRow(groupName)).ToBeVisibleAsync();

        // Second creation with the same name (case- and accent-insensitive
        // match per FR-001) — the Process detail re-renders with a flash error
        // and no second row appears.
        await procPage.CreateGroupAsync(groupName.ToLowerInvariant());
        await Expect(procPage.FlashError).ToContainTextAsync(new Regex("[Yy]a existe"));
    }

    [Test]
    public async Task Admin_CreatesGroup_SameNameUnderDifferentProcess_IsAllowed()
    {
        // Group names are unique PER PROCESS, not globally: the same name may be
        // reused by a group in a different Process (even within the same Fund).
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var groupName = $"GX-{unique}";

        var procPage = new ProcessAdminPage(Page);

        // Process #1 gets the group.
        await CreateProcessAndOpenAsync(procPage, $"{unique}a");
        await procPage.CreateGroupAsync(groupName);
        await Expect(procPage.GroupRow(groupName)).ToBeVisibleAsync();

        // Process #2 (same Fund) accepts the SAME group name — no collision.
        await CreateProcessAndOpenAsync(procPage, $"{unique}b");
        await procPage.CreateGroupAsync(groupName);
        await Expect(procPage.GroupRow(groupName)).ToBeVisibleAsync();
        await Expect(procPage.FlashError).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_RenamesGroup_RowReflectsNewName()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);
        var original = $"GC-{unique}";
        var renamed = $"GC2-{unique}";

        var procPage = new ProcessAdminPage(Page);
        await CreateProcessAndOpenAsync(procPage, unique);
        await procPage.CreateGroupAsync(original);
        await Expect(procPage.GroupRow(original)).ToBeVisibleAsync();

        var page = new AdminGroupsPage(Page);
        await page.GoToIndexAsync(BaseUrl);
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
