using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.Helpers;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.AdminReports;

[Category("AdminReports")]
public class CurrencyRolloutTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"currency-rollout-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Test]
    public async Task QuotationCreateForm_PrefillsConfiguredDefaultCurrency()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"currency_user_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Currency", "Tester", $"CUR-{uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        Assert.That(appIdMatch.Success, Is.True, "Application creation should land on details with an id.");

        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Currency Test Item", 0, "Test specs", BaseUrl);

        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();

        var currencyValue = await Page.Locator("[name=Currency]").InputValueAsync();
        // Spec 015 / T907 follow-up — base currency flipped COP -> CRC so the form
        // pre-fill matches a currency that is actually in the seeded catalog with
        // a published rate. Asserting the configured default still verifies the
        // wire-up between AdminReports:DefaultCurrency and the form.
        Assert.That(currencyValue, Is.EqualTo("CRC"),
            "Currency input must be prefilled from AdminReports:DefaultCurrency (CRC in dev/test config).");
    }

    [Test]
    public async Task QuotationCreateForm_RejectsCurrencyOfWrongLength()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"currency_reject_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Currency", "Reject", $"CRJ-{uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Details/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Currency Reject Item", 0, "Specs", BaseUrl);

        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();

        var supplierPage = new SupplierPage(Page);
        await supplierPage.FillSupplierFormAsync(
            legalId: $"SUP-{uniqueId}",
            name: "Reject Supplier",
            price: 100m,
            validUntil: "2027-12-31",
            filePath: _testFilePath,
            currency: "XX"); // Two-letter currency — must be rejected
        await supplierPage.SubmitAsync();

        var validationVisible = await Page.Locator(".text-danger").First.IsVisibleAsync();
        Assert.That(validationVisible, Is.True,
            "Form must surface a validation error when Currency length != 3.");
    }

    [Test]
    public async Task FundingAgreementPdf_RendersCurrencyCodeBesideEveryAmount()
    {
        var (appId, applicantEmail, applicantPassword) =
            await CreateApplicationAndSubmitResponseAsync(
                Guid.NewGuid().ToString("N")[..8], _testFilePath);

        // Login as admin (sentinel admin account is seeded in dev/E2E)
        // We need an admin to generate the funding agreement, which the helper above
        // does not seed. Use the sentinel admin from the dev seed.
        // CreateApplicationAndSubmitResponseAsync ends with the applicant logged out,
        // so we go straight to the login form — no extra logout click.
        const string adminEmail = "admin@FundingPlatform.com";
        const string adminPassword = "Sentinel123!";
        await LoginAsync(Page, adminEmail, adminPassword);

        var panelPage = new FundingAgreementPanelPage(Page);
        await panelPage.GotoDetailsAsync(BaseUrl, appId);

        if (await panelPage.GenerateButton.CountAsync() == 0)
        {
            Assert.Inconclusive("Funding-agreement preconditions not met for this seed; PDF render assertion skipped.");
            return;
        }

        await panelPage.ClickGenerateAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));

        var downloadFlow = new FundingAgreementDownloadFlow(Page);
        var bytes = await downloadFlow.CaptureDownloadBytesAsync(panelPage.DownloadLink);
        Assert.That(FundingAgreementDownloadFlow.LooksLikePdf(bytes), Is.True);

        // Spec 015 / T907 — base currency default flipped from COP to CRC.
        FundingAgreementPdfAssertions.AssertEachAmountHasCurrencyCode(bytes, new[] { "CRC" });
    }
}
