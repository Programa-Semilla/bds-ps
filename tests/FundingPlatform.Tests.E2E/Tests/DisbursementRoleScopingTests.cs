using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 045 / US5 — role scoping + read-only visibility (SC-008). A Financial Operator acts
/// only within its groups (out-of-group → flat 404, no disclosure); an Auditor sees the
/// surface read-only (no write controls); an applicant is refused.
/// </summary>
[Category("DisbursementRoleScoping")]
public class DisbursementRoleScopingTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string TempPwd = "TempPass1!";
    private readonly List<string> _seeded = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var p in _seeded)
        {
            if (File.Exists(p)) File.Delete(p);
        }
        _seeded.Clear();
    }

    private async Task Logout()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    [Test]
    public async Task Scoping_InGroupActs_OutOfGroup404_AuditorReadOnly_ApplicantRefused()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];

        // Admin builds a process with two non-overlapping groups.
        var adminEmail = $"db_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Pwd, "Db", "Admin", $"DBA-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Pwd);

        var groupA = $"DBA-{unique}";
        var groupB = $"DBB-{unique}";
        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync($"DBProc-{unique}");
        var processId = await procPage.OpenProcessDetailByNameAsync(BaseUrl, $"DBProc-{unique}");
        foreach (var g in new[] { groupA, groupB })
        {
            await procPage.GoToDetailsAsync(BaseUrl, processId);
            await procPage.CreateGroupAsync(g);
        }

        // Applicant in group A only.
        var applicantEmail = $"db_app_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        var formPage = new AdminUserFormPage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Db", "Applicant", applicantEmail, null, "Applicant", TempPwd,
            IdentificationData.CedulaFisica($"DBAPP-{unique}"));
        await formPage.SelectGroupsAsync(groupA);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // In-group Financial Operator (group A).
        var inOpEmail = $"db_inop_{unique}@example.com";
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("In", "Operator", inOpEmail, null, "Financial Operator", TempPwd, null);
        await formPage.SelectGroupsAsync(groupA);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Out-of-group Financial Operator (group B).
        var outOpEmail = $"db_outop_{unique}@example.com";
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Out", "Operator", outOpEmail, null, "Financial Operator", TempPwd, null);
        await formPage.SelectGroupsAsync(groupB);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Auditor (all groups → in scope, read-only).
        var auditorEmail = $"db_auditor_{unique}@example.com";
        await RegisterUserAsync(Page, auditorEmail, Pwd, "Db", "Auditor", $"DBAUD-{unique}");
        await AssignRoleAsync(auditorEmail, "Auditor");
        await Logout();

        // Applicant onboards + creates a draft (anchored to group A).
        await OnboardAndLoginAsync(applicantEmail, "AppPass1!");
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
        await Logout();

        // Seed to AgreementExecuted + a known allocation.
        _seeded.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, adminEmail, applicantEmail, inOpEmail, CreateBlobServiceClient()));
        await DisbursementSeeder.SeedAllocationAsync(ConnectionString, appId, 1_000_000m, adminEmail);

        var disb = new DisbursementPage(Page);

        // In-group operator can act (200 + write form present).
        await OnboardAndLoginAsync(inOpEmail, "InPass1!");
        Assert.That(await disb.GotoStatusAsync(BaseUrl, appId), Is.EqualTo(200));
        await Expect(disb.RecordForm).ToBeVisibleAsync();
        await Logout();

        // Out-of-group operator → flat 404 (no disclosure).
        await OnboardAndLoginAsync(outOpEmail, "OutPass1!");
        Assert.That(await disb.GotoStatusAsync(BaseUrl, appId), Is.EqualTo(404),
            "Out-of-group Financial Operator must get a flat 404.");
        await Logout();

        // Auditor: read-only — surface visible (200), no record/write controls.
        // (Created via RegisterUserAsync with a password, so a plain login — not the invite flow.)
        await LoginAsync(Page, auditorEmail, Pwd);
        Assert.That(await disb.GotoStatusAsync(BaseUrl, appId), Is.EqualTo(200));
        await Expect(disb.Surface).ToBeVisibleAsync();
        await Expect(disb.RecordForm).ToHaveCountAsync(0);
        await Logout();

        // Applicant: refused by the role attribute (403 / AccessDenied).
        await OnboardAndLoginAsync(applicantEmail, "AppPass1!");
        var status = await disb.GotoStatusAsync(BaseUrl, appId);
        var refused = status == 403 || Page.Url.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase);
        Assert.That(refused, Is.True, $"Applicant must be refused. Status={status}, Url={Page.Url}");
    }
}
