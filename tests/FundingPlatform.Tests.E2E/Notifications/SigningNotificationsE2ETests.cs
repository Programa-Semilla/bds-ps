using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 028 / US3 / T032 / SC-001 + SC-002 — convenio signing ceremony through the
/// real UI: generate → upload → approve (and a reject variant). Asserts the
/// applicant gets AGREEMENT_GENERATED / EXECUTED / REJECTED and the stage-group
/// reviewer gets SIGNED_UPLOAD_SUBMITTED (CTA /Review/SigningInbox). The rejection
/// body conveys "changes required" without the verbatim reviewer comment (NFR-003).
/// </summary>
public class SigningNotificationsE2ETests : PostResolutionNotificationsE2EBase
{
    private string _signedPdf = string.Empty;

    [SetUp]
    public void SetUpSignedPdf()
    {
        // A PDF-shaped payload (the signed-upload intake validates the %PDF- header).
        var prefix = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        var body = new byte[4096];
        Array.Copy(prefix, body, prefix.Length);
        _signedPdf = Path.Combine(Path.GetTempPath(), $"pr-signed-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_signedPdf, body);
    }

    [TearDown]
    public void TearDownSignedPdf()
    {
        if (File.Exists(_signedPdf)) File.Delete(_signedPdf);
    }

    [Test]
    public async Task Signing_generate_upload_approve_notifies_each_counterparty()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 028 / NFR-007 — smtp4dev sidecar not available.");
            return;
        }

        var (appId, adminEmail) = await GenerateAndUploadAsync();

        // Admin approves the signed upload → AGREEMENT_EXECUTED_APPLICANT.
        await LoginAsync(Page, adminEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        var panel = new SigningStagePanelPage(Page);
        await panel.ApprovePending();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        var executed = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Tu convenio fue ejecutado"));
        Assert.That(executed.Any(m => m.ToAddresses.Any(t => t.Contains(ApplicantEmail, StringComparison.OrdinalIgnoreCase))),
            Is.True, "AGREEMENT_EXECUTED_APPLICANT must reach the applicant.");
    }

    [Test]
    public async Task Signing_reject_notifies_applicant_without_reviewer_comment()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 028 / NFR-007 — smtp4dev sidecar not available.");
            return;
        }

        var (appId, adminEmail) = await GenerateAndUploadAsync();

        const string reviewerComment = "Falta la firma en la pagina tres del documento";

        // Admin rejects the signed upload → SIGNED_UPLOAD_REJECTED_APPLICANT.
        await LoginAsync(Page, adminEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        var panel = new SigningStagePanelPage(Page);
        await panel.RejectPending(reviewerComment);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        var rejected = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Tu convenio firmado requiere cambios"));
        var applicantMsg = rejected.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(ApplicantEmail, StringComparison.OrdinalIgnoreCase)));

        Assert.That(applicantMsg, Is.Not.Null, "SIGNED_UPLOAD_REJECTED_APPLICANT must reach the applicant.");
        Assert.That(applicantMsg!.HtmlBody + applicantMsg.TextBody, Does.Not.Contain(reviewerComment),
            "NFR-003: the rejection email must not embed the verbatim reviewer comment.");
    }

    /// <summary>
    /// Drives create→finalize→accept→generate→upload and asserts the
    /// AGREEMENT_GENERATED (applicant) and SIGNED_UPLOAD_SUBMITTED (reviewer)
    /// captures along the way. Returns the application id + the admin email.
    /// </summary>
    private async Task<(int AppId, string AdminEmail)> GenerateAndUploadAsync()
    {
        var (appId, itemId) = await DriveToResolvedAsync(rejectItem: false);

        // Applicant accepts the resolution → ResponseFinalized (generation precondition).
        await LoginAsync(Page, ApplicantEmail, Password);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();
        await LogoutAsync();

        var adminEmail = $"pr_adm_{UniqueId}@programa-semilla.test";
        await RegisterUserAsync(Page, adminEmail, Password, "Ada", "Admin", $"PRAD-{UniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");

        await MailCapture!.DrainAsync();

        // Admin generates the convenio → AGREEMENT_GENERATED_APPLICANT.
        await LoginAsync(Page, adminEmail, Password);
        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);
        await panel.ClickGenerateAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        var generated = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Tu convenio está listo para firmar"));
        Assert.That(generated.Any(m => m.ToAddresses.Any(t => t.Contains(ApplicantEmail, StringComparison.OrdinalIgnoreCase))),
            Is.True, "AGREEMENT_GENERATED_APPLICANT must reach the applicant.");
        await LogoutAsync();

        // Applicant uploads the signed convenio → SIGNED_UPLOAD_SUBMITTED_REVIEWER.
        await LoginAsync(Page, ApplicantEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        await Page.SetInputFilesAsync("[data-testid=signed-upload-file]", _signedPdf);
        await Page.Locator("[data-testid=signed-upload-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        var submitted = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Convenio firmado recibido para revisión"));
        var reviewerMsg = submitted.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(ReviewerEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(reviewerMsg, Is.Not.Null, "SIGNED_UPLOAD_SUBMITTED_REVIEWER must reach the stage-group reviewer.");
        Assert.That(reviewerMsg!.HtmlBody + reviewerMsg.TextBody, Does.Contain("/Review/SigningInbox"),
            "CTA must deep-link to the signing inbox.");
        await LogoutAsync();

        return (appId, adminEmail);
    }
}
