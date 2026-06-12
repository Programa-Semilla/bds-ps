using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 / US6 (FR-018) — required fields across applicant, admin and
/// reviewer forms carry the shared <c>_RequiredMark</c> (an asterisk with
/// <c>aria-label="campo obligatorio"</c>); optional fields carry none.
/// </summary>
[Category("ReviewFundingUx")]
public class RequiredMarkerTests : AuthenticatedTestBase
{
    private const string DefaultPassword = "Test123!";
    private const string MarkerSelector = "[aria-label='campo obligatorio']";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"rm-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    // Spec 032 — public registration removed; required-marker coverage on a live form
    // is provided by AdminUserCreateForm_RequiredMarked_OptionalNot below.

    [Test]
    public async Task AdminUserCreateForm_RequiredMarked_OptionalNot()
    {
        await LoginAsync(Page, "admin@programa-semilla.test", "Sentinel123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/Users/Create");

        // Required "Nombre" label carries the marker.
        var nameLabel = Page.Locator("label:has-text('Nombre')").First;
        await Expect(nameLabel.Locator(MarkerSelector)).ToHaveCountAsync(1);

        // Optional "Teléfono (opcional)" label carries none.
        var phoneLabel = Page.Locator("label:has-text('Teléfono (opcional)')");
        await Expect(phoneLabel.Locator(MarkerSelector)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ReviewerForm_ApplicantCodeField_ShowsMarker()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"rm_applicant_{uid}@example.com";
        var reviewerEmail = $"rm_reviewer_{uid}@example.com";

        var appId = await SubmitApplicationAsync(applicantEmail, uid);

        await RegisterUserAsync(Page, reviewerEmail, DefaultPassword, "RM", "Reviewer", $"RMR-{uid}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, DefaultPassword);

        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        var codeLabel = Page.Locator("label:has-text('Código del solicitante')");
        await Expect(codeLabel.Locator(MarkerSelector)).ToHaveCountAsync(1);
    }

    private async Task<int> SubmitApplicationAsync(string applicantEmail, string uid)
    {
        await RegisterUserAsync(Page, applicantEmail, DefaultPassword, "RM", "Applicant", $"RMP-{uid}");
        await LoginAsync(Page, applicantEmail, DefaultPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "RM Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var link = Page.Locator("a:has-text('Agregar proveedor')").First;
        await link.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"RM1-{uid}", "Supplier A", 900m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();
        link = Page.Locator("a:has-text('Agregar proveedor')").First;
        await link.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"RM2-{uid}", "Supplier B", 1100m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        return appId;
    }
}
