using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 041 — funds-usage evidence inbox + process-close read-only gate.
/// US1: the inbox lists executed apps in active processes and links to their
/// evidence page. US2: closing the governing Process de-lists the app and makes
/// its evidence page read-only (writes rejected server-side). US3: applicants and
/// out-of-group reviewers are refused with no disclosure.
///
/// Tests that close a Process use an ISOLATED process/group so the shared E2E
/// fixture's other suites are unaffected (mirrors FundsUsageEvidenceTests US4).
/// </summary>
[Category("EvidenceInbox")]
public class EvidenceInboxTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string TempPwd = "TempPass1!";
    private const string ApplicantPwd = "AppPass1!";
    private const string ReviewerPwd = "RevPass1!";

    private string _pdfPath = string.Empty;
    private readonly List<string> _seededFiles = [];

    [SetUp]
    public void SetUp()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        _pdfPath = Path.Combine(Path.GetTempPath(), $"evidencia-{stamp}.pdf");
        File.WriteAllBytes(_pdfPath, "%PDF-1.4\nfunds-usage evidence\n%%EOF\n"u8.ToArray());
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in new[] { _pdfPath }.Concat(_seededFiles))
        {
            if (File.Exists(p)) File.Delete(p);
        }
        _seededFiles.Clear();
    }

    private async Task Logout()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    private static string AppNumber(int appId) => $"APP-{appId:D5}";

    // ---------------- US1 ----------------

    [Test]
    public async Task US1_Inbox_ListsExecutedActiveApp_AndOpensEvidence()
    {
        // SeedExecutedApplicationAsync builds an executed app and a reviewer that is
        // in every group (in-scope), reusing the spec-036 seeding path.
        var (appId, reviewerEmail, _) = await SeedExecutedApplicationAsync();
        await LoginAsync(Page, reviewerEmail, Pwd);

        var inbox = new EvidenceInboxPage(Page);
        await inbox.GotoAsync(BaseUrl);

        // SC-001 — the sidebar entry exists, and the app is reachable from the inbox.
        await Expect(inbox.SidebarEntry("evidence-inbox")).ToBeVisibleAsync();
        await Expect(inbox.RowFor(AppNumber(appId))).ToBeVisibleAsync();

        await inbox.OpenAsync(AppNumber(appId));
        await Expect(Page).ToHaveURLAsync(new Regex($@"/Applications/{appId}/Evidence"));

        // Full spec-036 behavior available (active process).
        var evidence = new FundsUsageEvidencePage(Page);
        await Expect(evidence.Stage).ToBeVisibleAsync();
        await evidence.UploadAsync(_pdfPath);
        await Expect(evidence.SuccessToast).ToBeVisibleAsync();
        await Expect(evidence.RowFor(Path.GetFileName(_pdfPath))).ToBeVisibleAsync();
    }

    [Test]
    public async Task US1_Inbox_EmptyForReviewerWithNoQualifyingApps()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var group = $"EIE-{unique}";

        await RegisterAdminAndLoginAsync(unique);
        await AdminCreateProcessWithGroupsAsync($"EIEProc-{unique}", group);

        // Reviewer in an isolated group that has no executed applications.
        var reviewerEmail = $"eie_rev_{unique}@example.com";
        await AdminCreateUserAsync("Eie", "Reviewer", reviewerEmail, "Reviewer", null, group);
        await Logout();

        await OnboardAndLoginAsync(reviewerEmail, ReviewerPwd);
        var inbox = new EvidenceInboxPage(Page);
        await inbox.GotoAsync(BaseUrl);

        await Expect(inbox.Empty).ToBeVisibleAsync();
        await Expect(inbox.Rows).ToHaveCountAsync(0);
    }

    // ---------------- US2 ----------------

    [Test]
    public async Task US2_ClosedProcess_DeListed_ReadOnly_AndMutationRejected()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var group = $"EIC-{unique}";

        var adminEmail = await RegisterAdminAndLoginAsync(unique);
        var processId = await AdminCreateProcessWithGroupsAsync($"EICProc-{unique}", group);

        var applicantEmail = $"eic_app_{unique}@example.com";
        await AdminCreateUserAsync("Eic", "Applicant", applicantEmail, "Applicant",
            IdentificationData.CedulaFisica($"EICAPP-{unique}"), group);

        var reviewerEmail = $"eic_rev_{unique}@example.com";
        await AdminCreateUserAsync("Eic", "Reviewer", reviewerEmail, "Reviewer", null, group);
        await Logout();

        // Applicant (in the isolated group) creates a draft; we fast-forward it to executed.
        var appId = await ApplicantCreateDraftAppAsync(applicantEmail);
        await Logout();
        _seededFiles.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, adminEmail, applicantEmail, reviewerEmail, CreateBlobServiceClient()));

        // While ACTIVE: reviewer sees the app in the inbox and uploads one item.
        await OnboardAndLoginAsync(reviewerEmail, ReviewerPwd);
        var inbox = new EvidenceInboxPage(Page);
        await inbox.GotoAsync(BaseUrl);
        await Expect(inbox.RowFor(AppNumber(appId))).ToBeVisibleAsync();

        var evidence = new FundsUsageEvidencePage(Page);
        await evidence.GotoAsync(BaseUrl, appId);
        await evidence.UploadAsync(_pdfPath);
        await Expect(evidence.Rows).ToHaveCountAsync(1);

        // Capture an antiforgery token + the evidence id for crafted POSTs (FR-007).
        var token = await Page.Locator(
            "[data-testid=evidence-upload-form] input[name=__RequestVerificationToken]")
            .First.GetAttributeAsync("value");
        var evidenceId = await evidence.Rows.First.GetAttributeAsync("data-evidence-id");
        await Logout();

        // Admin closes the governing Process.
        await LoginAsync(Page, adminEmail, Pwd);
        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToDetailsAsync(BaseUrl, processId);
        await procPage.CloseAsync();
        await Logout();

        // Reviewer again: the app has dropped off the inbox (FR-004) ...
        await LoginAsync(Page, reviewerEmail, ReviewerPwd);
        await inbox.GotoAsync(BaseUrl);
        await Expect(inbox.RowFor(AppNumber(appId))).ToHaveCountAsync(0);

        // ... and the evidence page is read-only: loads (no 404), notice shown, no
        // write controls, download still present (FR-006).
        Assert.That(await evidence.GotoStatusAsync(BaseUrl, appId), Is.EqualTo(200));
        await Expect(Page.Locator("[data-testid=evidence-readonly-notice]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=evidence-upload-form]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=evidence-note-save]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=evidence-delete]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=evidence-download]").First).ToBeVisibleAsync();

        // FR-007 / SC-003 — crafted Upload + Delete POSTs are rejected with no change.
        await CraftedPostAsync($"{BaseUrl}/Applications/{appId}/Evidence/Upload", token!);
        await CraftedPostAsync($"{BaseUrl}/Applications/{appId}/Evidence/{evidenceId}/Delete", token!);

        await evidence.GotoAsync(BaseUrl, appId);
        await Expect(evidence.Rows).ToHaveCountAsync(1); // unchanged: nothing added, nothing deleted
    }

    // ---------------- US3 ----------------

    [Test]
    public async Task US3_Inbox_OutOfGroupReviewer_AndApplicant_Refused()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var groupA = $"EIA-{unique}";
        var groupB = $"EIB-{unique}";

        var adminEmail = await RegisterAdminAndLoginAsync(unique);
        await AdminCreateProcessWithGroupsAsync($"EIProc-{unique}", groupA, groupB);

        // Applicant in group A; reviewer in group B (no overlap).
        var applicantEmail = $"ei_app_{unique}@example.com";
        await AdminCreateUserAsync("Ei", "Applicant", applicantEmail, "Applicant",
            IdentificationData.CedulaFisica($"EIAPP-{unique}"), groupA);
        var reviewerEmail = $"ei_rev_{unique}@example.com";
        await AdminCreateUserAsync("Ei", "Reviewer", reviewerEmail, "Reviewer", null, groupB);
        await Logout();

        var appId = await ApplicantCreateDraftAppAsync(applicantEmail);
        await Logout();
        _seededFiles.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, adminEmail, applicantEmail, reviewerEmail, CreateBlobServiceClient()));

        var inbox = new EvidenceInboxPage(Page);
        var evidence = new FundsUsageEvidencePage(Page);

        // Out-of-group reviewer (B): the group-A app is NOT in their inbox (NFR-001),
        // and the evidence page is a flat 404 (no disclosure, FR-008).
        await OnboardAndLoginAsync(reviewerEmail, ReviewerPwd);
        await inbox.GotoAsync(BaseUrl);
        await Expect(inbox.RowFor(AppNumber(appId))).ToHaveCountAsync(0);
        Assert.That(await evidence.GotoStatusAsync(BaseUrl, appId), Is.EqualTo(404));
        await Logout();

        // Applicant: never offered the sidebar entry, and refused at /Evidence by the
        // role gate (FR-001/FR-008).
        await OnboardAndLoginAsync(applicantEmail, ApplicantPwd);
        await Page.GotoAsync(BaseUrl);
        await Expect(inbox.SidebarEntry("evidence-inbox")).ToHaveCountAsync(0);
        var status = await inbox.GotoStatusAsync(BaseUrl);
        var refused = status == 403
            || Page.Url.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase);
        Assert.That(refused, Is.True, $"Applicant must be refused the inbox. Status={status}, Url={Page.Url}");
    }

    // ---------------- helpers ----------------

    /// <summary>Reuses the spec-036 path: drives an app to AgreementExecuted and returns
    /// the in-group (all-groups) reviewer's credentials (logs in with <see cref="Pwd"/>).</summary>
    private async Task<(int appId, string reviewerEmail, string applicantEmail)> SeedExecutedApplicationAsync()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var quotationPath = Path.Combine(Path.GetTempPath(), $"q-{uniqueId}.pdf");
        File.WriteAllText(quotationPath, "Quotation placeholder");
        _seededFiles.Add(quotationPath);

        var (appId, applicantEmail, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, quotationPath);
        var reviewerEmail = $"seed_reviewer_{uniqueId}@example.com";
        var adminEmail = $"seed_admin_{uniqueId}@example.com";

        _seededFiles.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, adminEmail, applicantEmail, reviewerEmail, CreateBlobServiceClient()));

        return (appId, reviewerEmail, applicantEmail);
    }

    private async Task<string> RegisterAdminAndLoginAsync(string unique)
    {
        var adminEmail = $"ei_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Pwd, "Ei", "Admin", $"EIADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Pwd);
        return adminEmail;
    }

    /// <summary>Admin must be logged in. Creates an isolated Process and its groups; returns the Process id.</summary>
    private async Task<int> AdminCreateProcessWithGroupsAsync(string procName, params string[] groups)
    {
        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(procName);
        var processId = await procPage.OpenProcessDetailByNameAsync(BaseUrl, procName);
        foreach (var g in groups)
        {
            await procPage.GoToDetailsAsync(BaseUrl, processId);
            await procPage.CreateGroupAsync(g);
        }
        return processId;
    }

    /// <summary>Admin must be logged in. Creates a user (passwordless invite) in one group.</summary>
    private async Task AdminCreateUserAsync(
        string first, string last, string email, string role, string? legalId, string group)
    {
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(first, last, email, null, role, TempPwd, legalId);
        await new AdminUserFormPage(Page).SelectGroupsAsync(group);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();
    }

    private async Task<int> ApplicantCreateDraftAppAsync(string applicantEmail)
    {
        await OnboardAndLoginAsync(applicantEmail, ApplicantPwd);
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        return int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
    }

    /// <summary>Issues a same-origin authenticated POST (carries the browser session
    /// cookies) with a captured antiforgery token — exercises the server-side
    /// read-only rejection for crafted requests that bypass the hidden UI (FR-007).</summary>
    private async Task CraftedPostAsync(string url, string token)
        => await Page.EvaluateAsync(
            @"async ([url, token]) => {
                const fd = new FormData();
                fd.append('__RequestVerificationToken', token);
                try { await fetch(url, { method: 'POST', body: fd }); } catch (e) { /* rejection is the point */ }
            }",
            new object[] { url, token });
}
