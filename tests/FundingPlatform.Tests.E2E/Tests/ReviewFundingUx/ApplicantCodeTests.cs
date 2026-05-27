using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 / US5 (SC-004) — a reviewer sets the applicant code on the first
/// review screen (<c>/Review/{id}</c>); it persists on the applicant's account
/// and renders read-only on that applicant's profile.
/// </summary>
[Category("ReviewFundingUx")]
public class ApplicantCodeTests : AuthenticatedTestBase
{
    private string _quotationFilePath = string.Empty;
    private const string DefaultPassword = "Test123!";

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"ac-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    [Test]
    public async Task ReviewerSetsApplicantCode_RendersReadOnlyOnApplicantProfile()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"ac_applicant_{uid}@example.com";
        var reviewerEmail = $"ac_reviewer_{uid}@example.com";
        const string code = "COD-2026-XYZ";

        var appId = await SubmitApplicationAsApplicantAsync(applicantEmail, uid);

        // Reviewer sets the code on the first review screen.
        await RegisterUserAsync(Page, reviewerEmail, DefaultPassword, "Code", "Reviewer", $"ACR-{uid}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, DefaultPassword);

        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        var input = Page.Locator("[data-testid=applicant-code-input]");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(code);
        await Page.Locator("[data-testid=applicant-code-save]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($@"/Review/{appId}\b"));
        await Logout();

        // Applicant sees it read-only on their profile: the value is the code I set,
        // and the input is disabled + readonly (administrado), not editable.
        await LoginAsync(Page, applicantEmail, DefaultPassword);
        var profile = new ProfilePage(Page);
        await profile.GotoAsync(BaseUrl);
        await Expect(profile.CodigoPersonalField).ToHaveValueAsync(code);
        await Expect(profile.CodigoPersonalField).ToBeDisabledAsync();
        Assert.That(await profile.CodigoPersonalField.GetAttributeAsync("readonly"),
            Is.Not.Null, "Código del solicitante must be read-only on the profile.");
    }

    private async Task<int> SubmitApplicationAsApplicantAsync(string applicantEmail, string uid)
    {
        await RegisterUserAsync(Page, applicantEmail, DefaultPassword, "Code", "Applicant", $"ACP-{uid}");
        await LoginAsync(Page, applicantEmail, DefaultPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Code Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var link = Page.Locator("a:has-text('Agregar proveedor')").First;
        await link.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"AC1-{uid}", "Supplier A", 900m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        link = Page.Locator("a:has-text('Agregar proveedor')").First;
        await link.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"AC2-{uid}", "Supplier B", 1100m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Logout();
        return appId;
    }

    private async Task Logout() =>
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
}
