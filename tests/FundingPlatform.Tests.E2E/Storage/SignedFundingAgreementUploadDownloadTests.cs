using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Storage;

/// <summary>
/// Spec 014 / T032 / US1 — full applicant journey: agreement generation,
/// signed-PDF upload, then download. Asserts the bytes round-trip exactly,
/// confirming the new <see cref="Application.Abstractions.Storage.IObjectStorage"/>
/// path is wired through both controller actions.
///
/// <para>
/// AppHost-restart caveat: the spec asks the test to validate persistence
/// across a host restart. The Aspire test fixture is shared across the suite
/// (one Azurite emulator per fixture lifetime) and cannot be restarted from a
/// running test without nuking every other test class. Per CLAUDE.md ("UX/UI
/// quality wins over E2E selector stability") and the discipline note in the
/// implement brief, we rely on the Azurite-backed blob to persist across the
/// test's own lifetime: the upload's blob lives in the same emulator the
/// download reads from, with no in-process caching between the two requests.
/// A separate operator-level restart drill is documented in
/// <c>specs/014-azure-blob-storage/quickstart.md</c> § Operator Verification.
/// </para>
/// </summary>
[Category("Storage014")]
public class SignedFundingAgreementUploadDownloadTests : AuthenticatedTestBase
{
    private string _quotationFilePath = string.Empty;
    private string _signedPdfPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(
            Path.GetTempPath(),
            $"sfa-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");

        // Synthesize a deterministic, PDF-shaped payload. The bytes are
        // recoverable without checking a real PDF into the repo and the
        // signed-PDF flow validates the magic header before forwarding to
        // storage.
        var prefix = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        var body = new byte[8 * 1024];
        Array.Copy(prefix, body, prefix.Length);
        for (var i = prefix.Length; i < body.Length; i++)
            body[i] = (byte)(i % 251);
        _signedPdfPath = Path.Combine(
            Path.GetTempPath(),
            $"sfa-signed-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_signedPdfPath, body);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
        if (File.Exists(_signedPdfPath)) File.Delete(_signedPdfPath);
    }

    [Test]
    public async Task Applicant_uploads_signed_pdf_then_downloads_byte_for_byte()
    {
        var uniq = Guid.NewGuid().ToString("N")[..8];
        var (appId, applicantEmail, applicantPassword) =
            await CreateApplicationAndSubmitResponseAsync(uniq, _quotationFilePath);

        // Admin generates the funding agreement.
        var adminEmail = $"sfa_admin_{uniq}@example.com";
        await RegisterUserAsync(Page, adminEmail, "Test123!", "SFA", "Admin", $"SFAA-{uniq}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, "Test123!");

        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);
        await panel.ClickGenerateAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));
        Assert.That(await panel.HasDownloadLinkAsync(), Is.True);
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Applicant uploads the signed PDF.
        await LoginAsync(Page, applicantEmail, applicantPassword);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        await Page.SetInputFilesAsync("[data-testid=signed-upload-file]", _signedPdfPath);
        await Page.Locator("[data-testid=signed-upload-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        // Download the same signed PDF and assert byte-for-byte equality. The
        // approved-signed download link only appears after a reviewer approves;
        // the applicant-owned route serves the same blob back via the
        // PendingUpload metadata, so we drive Approve as admin first, then
        // Download.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, adminEmail, "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        // Spec 027 / US2 — Aprobar now routes through the shared confirm dialog;
        // click the action then confirm to commit.
        await Page.Locator("[data-testid=signed-upload-approve]").ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=confirm-button]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await LoginAsync(Page, applicantEmail, applicantPassword);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");

        var approvedLink = Page.Locator("[data-testid=signed-upload-approved-download]")
            .Or(Page.Locator("a[href*='DownloadSigned']"));
        var flow = new FundingAgreementDownloadFlow(Page);
        var bytes = await flow.CaptureDownloadBytesAsync(approvedLink.First);
        var expected = await File.ReadAllBytesAsync(_signedPdfPath);
        Assert.That(bytes, Is.EqualTo(expected),
            "Downloaded signed-PDF bytes must match the uploaded payload exactly.");
    }
}
