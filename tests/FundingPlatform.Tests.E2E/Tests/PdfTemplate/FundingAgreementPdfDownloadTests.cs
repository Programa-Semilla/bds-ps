using System.Diagnostics;
using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.PdfTemplate;

/// <summary>
/// Spec 018 / SC-010 — drives a funder operator from the Application detail
/// page through PDF generation and download, then asserts the rendered text
/// layer contains the four expected section headings and that the legacy
/// "MARCADOR DE POSICIÓN" banner is absent (SC-006).
/// </summary>
[Category("FundingAgreement")]
[Category("Spec018")]
public class FundingAgreementPdfDownloadTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;
    private string _applicantEmail = string.Empty;
    private string _applicantPassword = "Test123!";
    private string _reviewerEmail = string.Empty;
    private string _adminEmail = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"fa-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    [Test]
    public async Task BrandedPdf_ContainsExpectedSectionHeadings_AndAbsentLegacyBanner()
    {
        var (appId, _) = await SetupAcceptedApplicationAsync();

        await LoginAsync(Page, _adminEmail, "Test123!");

        var panelPage = new FundingAgreementPanelPage(Page);
        await panelPage.GotoDetailsAsync(BaseUrl, appId);
        await panelPage.ClickGenerateAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        Assert.That(await panelPage.HasDownloadLinkAsync(), Is.True,
            "Download link must appear after successful generation.");

        var downloadFlow = new FundingAgreementDownloadFlow(Page);
        var bytes = await downloadFlow.CaptureDownloadBytesAsync(panelPage.DownloadLink);

        Assert.That(FundingAgreementDownloadFlow.LooksLikePdf(bytes), Is.True,
            "Downloaded bytes must have a %PDF- header.");

        // Persist the bytes to a temp file so we can run pdftotext against it.
        var pdfPath = Path.Combine(Path.GetTempPath(), $"branded-pdf-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdfPath, bytes);
        try
        {
            var text = ExtractText(pdfPath);

            // FR-008 / FR-009 / FR-010 / FR-011 — required section headings on
            // the text layer (verifies the new branded structure shipped).
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("Recursos solicitados"),
                    "Requested-resources section heading must be present in the text layer.");
                Assert.That(text, Does.Contain("Resultados comisión"),
                    "Committee-results section heading must be present in the text layer.");
                Assert.That(text, Does.Contain("Información empresas proveedoras"),
                    "Supplier-verification section heading must be present.");
                Assert.That(text, Does.Contain("DECLARO BAJO LA FE DEL JURAMENTO"),
                    "Sworn-declaration heading must be present.");
                // SC-006 — legacy placeholder banner must NOT appear anywhere.
                Assert.That(text, Does.Not.Contain("MARCADOR DE POSICIÓN"),
                    "Legacy placeholder banner must be absent.");
            });
        }
        finally
        {
            if (File.Exists(pdfPath))
                File.Delete(pdfPath);
        }
    }

    /// <summary>
    /// Runs `pdftotext` against the supplied PDF path and returns the text
    /// layer. If `pdftotext` is unavailable on the test runner, marks the test
    /// inconclusive — visual fidelity is verified manually per SC-001.
    /// </summary>
    private static string ExtractText(string pdfPath)
    {
        try
        {
            var psi = new ProcessStartInfo("pdftotext", $"-layout \"{pdfPath}\" -")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Inconclusive("pdftotext is not available on this test runner.");
            return string.Empty;
        }
    }

    private async Task<(int AppId, int ItemId)> SetupAcceptedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _applicantEmail = $"pdftest_applicant_{_uniqueId}@example.com";
        _reviewerEmail = $"pdftest_reviewer_{_uniqueId}@example.com";
        _adminEmail = $"pdftest_admin_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, _adminEmail, "Test123!", "Admin", "PdfTest", $"PALID-{_uniqueId}");
        await AssignRoleAsync(_adminEmail, "Admin");

        await RegisterUserAsync(Page, _applicantEmail, _applicantPassword, "Pdf", "Applicant", $"PAALID-{_uniqueId}");
        await LoginAsync(Page, _applicantEmail, _applicantPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync("Sazón Vegetariano");

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Laptop Test", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"PT1-{_uniqueId}", "Supplier PT1", 900m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"PT2-{_uniqueId}", "Supplier PT2", 1100m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        var impactButton = Page.Locator("a:has-text('Impacto')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (int i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        await Page.Locator("button[type=submit]:has-text('Enviar solicitud')").ClickAsync();
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await RegisterUserAsync(Page, _reviewerEmail, "Test123!", "Reviewer", "PdfTest", $"RVLID-{_uniqueId}");
        await AssignRoleAsync(_reviewerEmail, "Reviewer");
        await LoginAsync(Page, _reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItemCard = reviewPage.ItemCards.First;
        var itemId = int.Parse(await firstItemCard.GetAttributeAsync("data-item-id") ?? "0");

        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var supplierOption = supplierDropdown.Locator("option").Nth(1);
        var supplierValue = await supplierOption.GetAttributeAsync("value");
        await supplierDropdown.SelectOptionAsync(supplierValue!);
        await reviewPage.ItemLineCodeInput(itemId).FillAsync("T1-1");
        await reviewPage.ItemSubmitButton(itemId).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/Review/{appId}"));

        await reviewPage.FinalizeButton.ClickAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Applicant accepts
        await LoginAsync(Page, _applicantEmail, _applicantPassword);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return (appId, itemId);
    }
}
