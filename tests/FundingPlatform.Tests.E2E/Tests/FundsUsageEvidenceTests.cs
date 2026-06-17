using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 036 — funds-usage evidence stage E2E across all four user stories:
/// US1 collect (upload/list/download), US2 annotate, US3 delete, US4 scoped access.
/// Uses <see cref="FundingAgreementSeeder.SeedExecutedAgreementAsync"/> to reach the
/// AgreementExecuted gate (research D8).
/// </summary>
[Category("FundsUsageEvidence")]
public class FundsUsageEvidenceTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string TempPwd = "TempPass1!";

    private string _pdfPath = string.Empty;
    private string _pngPath = string.Empty;
    private readonly List<string> _seededFiles = [];

    [SetUp]
    public void SetUp()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        _pdfPath = Path.Combine(Path.GetTempPath(), $"evidencia-{stamp}.pdf");
        _pngPath = Path.Combine(Path.GetTempPath(), $"imagen-{stamp}.png");
        File.WriteAllBytes(_pdfPath, "%PDF-1.4\nfunds-usage evidence\n%%EOF\n"u8.ToArray());
        File.WriteAllBytes(_pngPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52]);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in new[] { _pdfPath, _pngPath }.Concat(_seededFiles))
        {
            if (File.Exists(p)) File.Delete(p);
        }
        _seededFiles.Clear();
    }

    private async Task Logout()
    {
        // Navigate home first: a bare 403/404 refusal page has no navbar, so the
        // logout form is only reliably present on a laid-out page.
        await Page.GotoAsync(BaseUrl);
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    /// <summary>Drives an application to AgreementExecuted and returns its id plus the
    /// in-group reviewer's credentials (reviewer is in every group via RegisterUserAsync).</summary>
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

    // ---------------- US1 — collect evidence ----------------

    [Test]
    public async Task US1_Reviewer_Uploads_ListsWithMetadata_AndDownloads()
    {
        var (appId, reviewerEmail, _) = await SeedExecutedApplicationAsync();
        await LoginAsync(Page, reviewerEmail, Pwd);

        // The stage link is surfaced on the executed funding-agreement surface.
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        var stageLink = Page.Locator("[data-testid=evidence-stage-link]");
        await Expect(stageLink).ToBeVisibleAsync();
        await stageLink.ClickAsync();

        var evidence = new FundsUsageEvidencePage(Page);
        await Expect(evidence.Stage).ToBeVisibleAsync();
        await Expect(evidence.Empty).ToBeVisibleAsync();

        await evidence.UploadAsync(_pdfPath);
        await Expect(evidence.SuccessToast).ToBeVisibleAsync();
        await evidence.UploadAsync(_pngPath);

        var pdfName = Path.GetFileName(_pdfPath);
        var pngName = Path.GetFileName(_pngPath);
        await Expect(evidence.RowFor(pdfName)).ToBeVisibleAsync();
        await Expect(evidence.RowFor(pngName)).ToBeVisibleAsync();
        Assert.That(await evidence.Rows.CountAsync(), Is.EqualTo(2));

        // Uploader display name is shown on the row metadata.
        await Expect(evidence.RowFor(pdfName)).ToContainTextAsync("Seed Reviewer");

        // Download returns the original file.
        await evidence.DownloadRowAsync(pdfName);
    }

    // ---------------- US2 — annotate ----------------

    [Test]
    public async Task US2_Note_AddEdit_Persists_AndOversizeRejected()
    {
        var (appId, reviewerEmail, _) = await SeedExecutedApplicationAsync();
        await LoginAsync(Page, reviewerEmail, Pwd);

        var evidence = new FundsUsageEvidencePage(Page);
        await evidence.GotoAsync(BaseUrl, appId);
        await evidence.UploadAsync(_pdfPath);

        var pdfName = Path.GetFileName(_pdfPath);
        var note250 = new string('a', 250);

        await evidence.SaveNoteAsync(pdfName, note250);
        await Expect(evidence.SuccessToast).ToBeVisibleAsync();

        // Persists across reload.
        await evidence.GotoAsync(BaseUrl, appId);
        await Expect(evidence.RowFor(pdfName).Locator("[data-testid=evidence-note-edit]")).ToHaveValueAsync(note250);

        // Edit to a different value.
        await evidence.SaveNoteAsync(pdfName, "nota corregida");
        await evidence.GotoAsync(BaseUrl, appId);
        await Expect(evidence.RowFor(pdfName).Locator("[data-testid=evidence-note-edit]")).ToHaveValueAsync("nota corregida");

        // Oversize (>250) is rejected server-side with an es-CR error toast.
        await evidence.SaveOversizeNoteAsync(pdfName, new string('b', 251));
        await Expect(evidence.ErrorToast).ToBeVisibleAsync();
    }

    // ---------------- US3 — delete ----------------

    [Test]
    public async Task US3_Delete_WithConfirm_RemovesRow_CancelKeepsOthers()
    {
        var (appId, reviewerEmail, _) = await SeedExecutedApplicationAsync();
        await LoginAsync(Page, reviewerEmail, Pwd);

        var evidence = new FundsUsageEvidencePage(Page);
        await evidence.GotoAsync(BaseUrl, appId);
        await evidence.UploadAsync(_pdfPath);
        await evidence.UploadAsync(_pngPath);
        Assert.That(await evidence.Rows.CountAsync(), Is.EqualTo(2));

        var pdfName = Path.GetFileName(_pdfPath);
        var pngName = Path.GetFileName(_pngPath);

        // Cancel keeps everything.
        await evidence.DeleteThenCancelAsync(pdfName);
        Assert.That(await evidence.Rows.CountAsync(), Is.EqualTo(2));

        // Confirm removes the chosen row; the other remains.
        await evidence.DeleteWithConfirmAsync(pdfName);
        await Expect(evidence.SuccessToast).ToBeVisibleAsync();
        await Expect(evidence.RowFor(pdfName)).ToHaveCountAsync(0);
        await Expect(evidence.RowFor(pngName)).ToBeVisibleAsync();
    }

    // ---------------- US4 — scoped, reviewer-only access ----------------

    [Test]
    public async Task US4_Applicant_OutOfGroupReviewer_AndPreExecution_Get404()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];

        // Admin builds a process with two non-overlapping groups.
        var adminEmail = $"ev_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Pwd, "Ev", "Admin", $"EVA-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Pwd);

        var groupA = $"EVA-{unique}";
        var groupB = $"EVB-{unique}";
        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync($"EVProc-{unique}");
        var processId = await procPage.OpenProcessDetailByNameAsync(BaseUrl, $"EVProc-{unique}");
        foreach (var g in new[] { groupA, groupB })
        {
            await procPage.GoToDetailsAsync(BaseUrl, processId);
            await procPage.CreateGroupAsync(g);
        }

        // Applicant in group A only.
        var applicantEmail = $"ev_app_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Ev", "Applicant", applicantEmail, null, "Applicant", TempPwd,
            IdentificationData.CedulaFisica($"EVAPP-{unique}"));
        var formPage = new AdminUserFormPage(Page);
        await formPage.SelectGroupsAsync(groupA);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Reviewer in group B only (no overlap with the group-A applicant).
        var reviewerEmail = $"ev_rev_{unique}@example.com";
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync("Ev", "Reviewer", reviewerEmail, null, "Reviewer", TempPwd, null);
        await formPage.SelectGroupsAsync(groupB);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();
        await Logout();

        // Applicant onboards and creates two draft applications (anchored to group A).
        await OnboardAndLoginAsync(applicantEmail, "AppPass1!");
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var executedAppId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var draftAppId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
        await Logout();

        // Seed one of them to AgreementExecuted (the other stays a draft for the state-gate check).
        _seededFiles.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, executedAppId, adminEmail, applicantEmail, reviewerEmail, CreateBlobServiceClient()));

        var evidence = new FundsUsageEvidencePage(Page);

        // Applicant (no reviewer/admin role) is refused by the [Authorize(Roles=...)]
        // gate before the controller runs → 403 / AccessDenied (the codebase's
        // role-refusal convention; the controller's no-disclosure 404 is for the
        // in-scope cases below).
        await OnboardAndLoginAsync(applicantEmail, "AppPass1!");
        var applicantStatus = await evidence.GotoStatusAsync(BaseUrl, executedAppId);
        var applicantRefused = applicantStatus == 403
            || Page.Url.Contains("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase);
        Assert.That(applicantRefused, Is.True,
            $"Applicant must be refused the reviewer-only evidence stage. Status={applicantStatus}, Url={Page.Url}");
        await Logout();

        // Out-of-group reviewer (group B) → 404 (no disclosure) on the group-A app.
        await OnboardAndLoginAsync(reviewerEmail, "RevPass1!");
        Assert.That(await evidence.GotoStatusAsync(BaseUrl, executedAppId), Is.EqualTo(404),
            "Out-of-group reviewer must get a flat 404.");
        await Logout();

        // In-scope viewer (admin) on a NON-executed app → 404 (stage unavailable before execution).
        await LoginAsync(Page, adminEmail, Pwd);
        Assert.That(await evidence.GotoStatusAsync(BaseUrl, draftAppId), Is.EqualTo(404),
            "Evidence stage is unavailable before AgreementExecuted.");
    }
}
