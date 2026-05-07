using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US2 — applicant creates a CRC quotation through the real applicant
/// path:
///   Application/Details → "Agregar proveedor" → SupplierPage (legal-ID lookup
///   + new-supplier form) → quote section with currency dropdown defaulted to
///   CRC → submit → land back on Application/Details.
///
/// Properties exercised end-to-end:
///   1. The currency dropdown defaults to CRC.
///   2. The conversion-preview region stays hidden while CRC is selected (T211).
///   3. Saving 750_000 CRC produces a quotation row that renders only
///      "₡750,000.00 CRC" — NO conversion indicator, NO rate-snapshot box,
///      NO legacy-needs-review badge.
///
/// History note: the previous version of this test navigated directly to a now-
/// orphaned <c>/Application/{appId}/Item/{itemId}/Quotation/Add</c> URL — a route
/// no UI surface ever linked to. That bypassed the actual applicant journey.
/// The current test exercises the path the applicant clicks through in the real
/// product.
/// </summary>
public class ApplicantCrcQuoteE2E : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"crc-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\nplaceholder quotation\n%%EOF\n");
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
    public async Task CrcQuotation_GoldenPath_PreviewHidden_NoConversionIndicatorOnDetails()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"crc_app_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "CRC", "Applicant", $"CRC-{uniqueId}");
        await LoginAsync(Page, email, password);

        await CreateApplicationAndItemAsync();
        await ClickAddSupplierOnItemRowAsync();

        var supplierPage = new SupplierPage(Page);
        var supplierLegalId = $"SUP-CRC-{uniqueId}";
        var supplierName = $"CRC Supplier {uniqueId}";

        Assert.That(await supplierPage.SearchByLegalIdAsync(supplierLegalId), Is.EqualTo("Empty"));

        await supplierPage.FillNewSupplierFormAsync(
            name: supplierName,
            branchName: "Sede principal",
            province: "San Jose");

        // The dropdown defaults to CRC (the catalog's base + first option). Verify
        // before typing anything else.
        await Expect(supplierPage.CurrencyInput).ToHaveValueAsync("CRC");

        // T211 — preview must remain hidden while CRC is selected.
        await supplierPage.PriceInput.FillAsync("750000");
        await supplierPage.PriceInput.BlurAsync();
        await Expect(supplierPage.ConversionPreview).ToBeHiddenAsync();

        // Re-select CRC explicitly to exercise the change handler's hide path.
        await supplierPage.SetCurrencyAsync("CRC");
        await Expect(supplierPage.ConversionPreview).ToBeHiddenAsync();

        await supplierPage.ValidUntilInput.FillAsync("2027-12-31");
        await supplierPage.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await supplierPage.SubmitAsync();

        // Saved → redirected back to Application Details.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Detail page shows only the CRC amount + CRC currency. No USD/converted block.
        // Note: Intl/CultureInfo es-CR uses U+00A0 (non-breaking space) as the
        // thousands separator; \s in .NET regex matches it, plain ' ' does not.
        var quotationRow = Page.Locator("[data-testid=quotation-row]").First;
        await Expect(quotationRow).ToBeVisibleAsync();
        await Expect(quotationRow).ToContainTextAsync(new Regex(@"750[\.,\s]?000"));
        await Expect(quotationRow).ToContainTextAsync(new Regex("CRC"));

        // No conversion indicator: the rate-snapshot subblock must not render for a CRC quote.
        var snapshotBox = quotationRow.Locator("[data-testid=quotation-rate-snapshot]");
        await Expect(snapshotBox).ToHaveCountAsync(0);

        // No legacy-needs-review badge for a freshly-saved CRC quote.
        var legacyBadge = quotationRow.Locator("[data-testid=legacy-needs-review]");
        await Expect(legacyBadge).ToHaveCountAsync(0);

        // The row must not contain the literal "USD" — defensive against accidental
        // cross-currency rendering bugs (the only currency on the page should be CRC).
        var rowText = await quotationRow.InnerTextAsync();
        Assert.That(rowText, Does.Not.Contain("USD"),
            "CRC quotation row must not surface any USD text.");
    }

    private async Task CreateApplicationAndItemAsync()
    {
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "CRC Test Item", 0, "Specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
    }

    private async Task ClickAddSupplierOnItemRowAsync()
    {
        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Supplier/Add"));
    }
}
