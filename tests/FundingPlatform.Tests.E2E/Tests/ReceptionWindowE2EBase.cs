using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 044 — shared E2E setup for the reception-window suites. Builds an ISOLATED
/// Fund→Process→Group + an applicant scoped to only that group, so reception windows
/// configured here never affect the shared fixture's other suites (mirrors the
/// EvidenceInboxTests isolated-process pattern). Windows are seeded relative to real
/// <c>UtcNow</c> via <see cref="ReceptionWindowSeed"/> (no clock freeze, research D2).
/// </summary>
public abstract class ReceptionWindowE2EBase : AuthenticatedTestBase
{
    protected const string AdminPwd = "Test123!";
    protected const string TempPwd = "TempPass1!";
    protected const string ApplicantPwd = "AppPass1!";

    protected async Task<string> RegisterAdminAndLoginAsync(string unique)
    {
        var adminEmail = $"rw_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPwd, "Rw", "Admin", $"RWADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPwd);
        return adminEmail;
    }

    /// <summary>Admin must be logged in. Creates an isolated Process + one Group; returns the Process id.</summary>
    protected async Task<int> AdminCreateProcessWithGroupAsync(string procName, string group)
    {
        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(procName);
        var processId = await procPage.OpenProcessDetailByNameAsync(BaseUrl, procName);
        await procPage.GoToDetailsAsync(BaseUrl, processId);
        await procPage.CreateGroupAsync(group);
        return processId;
    }

    /// <summary>Admin must be logged in. Creates a passwordless-invite applicant in one group.</summary>
    protected async Task AdminCreateApplicantInGroupAsync(string email, string legalId, string group)
    {
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Rw", "Applicant", email, null, "Applicant", TempPwd,
            IdentificationData.CedulaFisica(legalId));
        await new AdminUserFormPage(Page).SelectGroupsAsync(group);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();
    }

    protected async Task Logout()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }
}
