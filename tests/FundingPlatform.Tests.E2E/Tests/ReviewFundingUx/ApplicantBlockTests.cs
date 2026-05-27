using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 / US3 — the funding-agreement page shows a richer applicant block
/// (company, representative, identification, email, phone, código, group,
/// submission date). Empty optional fields render the neutral "—". The block is
/// screen-only; the PDF document body is unchanged (FR-009) — guaranteed by the
/// untouched <c>Document.cshtml</c> and guarded by the PDF projection tests.
/// </summary>
[Category("ReviewFundingUx")]
public class ApplicantBlockTests : AuthenticatedTestBase
{
    private string _quotationFilePath = string.Empty;
    private const string DefaultPassword = "Test123!";

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"ab-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    [Test]
    public async Task FundingAgreementPage_ShowsApplicantBlock_WithDashForEmptyOptionals()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"ab_applicant_{uid}@example.com";
        var adminEmail = $"ab_admin_{uid}@example.com";

        await RegisterUserAsync(Page, adminEmail, DefaultPassword, "AB", "Admin", $"ABA-{uid}");
        await AssignRoleAsync(adminEmail, "Admin");

        // Applicant registered without a phone → phone renders "—".
        await RegisterUserAsync(Page, applicantEmail, DefaultPassword, "Bloque", "Solicitante", $"ABP-{uid}");
        await LoginAsync(Page, applicantEmail, DefaultPassword);
        var appId = await SubmitApplicationAsync(uid);
        await Logout();

        await LoginAsync(Page, adminEmail, DefaultPassword);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");

        var block = Page.Locator("[data-testid=fa-applicant-block]");
        await Expect(block).ToBeVisibleAsync();
        await Expect(block.Locator("[data-testid=fa-applicant-representative]")).ToContainTextAsync("Bloque Solicitante");
        await Expect(block.Locator("[data-testid=fa-applicant-email]")).ToContainTextAsync(applicantEmail);
        await Expect(block.Locator("[data-testid=fa-applicant-company]")).Not.ToBeEmptyAsync();
        await Expect(block.Locator("[data-testid=fa-applicant-group]")).Not.ToHaveTextAsync("—");

        // Empty optional → neutral placeholder, never a blank.
        await Expect(block.Locator("[data-testid=fa-applicant-phone]")).ToHaveTextAsync("—");
        await Expect(block.Locator("[data-testid=fa-applicant-codigo]")).ToHaveTextAsync("—");

        // Submission date present (the application was submitted).
        await Expect(block.Locator("[data-testid=fa-applicant-submitted]")).Not.ToHaveTextAsync("—");
    }

    private async Task<int> SubmitApplicationAsync(string uid)
    {
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "AB Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var link = Page.Locator("a:has-text('Agregar proveedor')").First;
        await link.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"AB1-{uid}", "Supplier A", 900m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        link = Page.Locator("a:has-text('Agregar proveedor')").First;
        await link.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"AB2-{uid}", "Supplier B", 1100m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        return appId;
    }

    private async Task Logout() =>
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
}
