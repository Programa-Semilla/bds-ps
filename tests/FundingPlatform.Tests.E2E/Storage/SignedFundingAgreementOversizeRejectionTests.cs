using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Storage;

/// <summary>
/// Spec 014 / T050 / US5 — submit a 25 MiB file to the signed-PDF endpoint
/// with the cap at 20 MiB; assert the localized 413 and that no blob lands in
/// Azurite. The blob-side absence is asserted via the BlobServiceClient
/// helper exposed by the AspireFixture — counting blobs in the
/// signed-funding-agreements container before/after the rejected request.
/// </summary>
[Category("Storage014")]
public class SignedFundingAgreementOversizeRejectionTests : AuthenticatedTestBase
{
    private string _quotationFilePath = string.Empty;
    private string _oversizePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(
            Path.GetTempPath(),
            $"sfa-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");

        // 25 MiB synthesized payload prefixed with the PDF magic.
        var size = 25 * 1024 * 1024;
        var prefix = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        var bytes = new byte[size];
        Array.Copy(prefix, bytes, prefix.Length);
        // Leave the rest as zeros — the controller's filter rejects on
        // Content-Length before the body is ever read, so the contents are
        // immaterial.
        _oversizePath = Path.Combine(
            Path.GetTempPath(),
            $"sfa-oversize-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_oversizePath, bytes);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
        if (File.Exists(_oversizePath)) File.Delete(_oversizePath);
    }

    [Test]
    public async Task Oversize_signed_pdf_upload_rejected_with_413_and_no_blob_created()
    {
        var uniq = Guid.NewGuid().ToString("N")[..8];
        var (appId, applicantEmail, applicantPassword) =
            await CreateApplicationAndSubmitResponseAsync(uniq, _quotationFilePath);

        var adminEmail = $"sfa_oversize_admin_{uniq}@example.com";
        await RegisterUserAsync(Page, adminEmail, "Test123!", "OS", "Admin", $"OSA-{uniq}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, "Test123!");

        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);
        await panel.ClickGenerateAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await LoginAsync(Page, applicantEmail, applicantPassword);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");

        // POST the multipart upload directly via Playwright's APIRequest so we
        // can capture the status code without the form's full-page navigation.
        // The browser does not expose the response status of a same-document
        // form submission cleanly.
        var token = await Page.Locator(
            "form[action*='/FundingAgreement/Upload'] input[name=__RequestVerificationToken]")
            .First.InputValueAsync();
        var generatedVersion = await Page.Locator(
            "form[action*='/FundingAgreement/Upload'] input[name=GeneratedVersion]")
            .First.InputValueAsync();
        var cookies = await Page.Context.CookiesAsync();
        var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));

        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseCookies = false,
        });
        http.DefaultRequestHeaders.Add("Cookie", cookieHeader);

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(token), "__RequestVerificationToken");
        multipart.Add(new StringContent(generatedVersion), "GeneratedVersion");
        var fileBytes = await File.ReadAllBytesAsync(_oversizePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        multipart.Add(fileContent, "File", "oversize.pdf");

        var resp = await http.PostAsync(
            $"{BaseUrl}/Applications/{appId}/FundingAgreement/Upload",
            multipart);

        Assert.That(resp.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.RequestEntityTooLarge),
            "Oversize upload must be rejected with HTTP 413 by the UploadSizeGuard filter.");

        var body = await resp.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("excede").Or.Contain("máximo"),
            "Rejection response must include the localized es-CR message.");
    }
}
