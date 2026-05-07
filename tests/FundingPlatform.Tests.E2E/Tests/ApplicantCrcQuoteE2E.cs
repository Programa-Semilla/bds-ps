using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US2 — applicant creates a CRC quotation. The CRC short-circuit
/// must hold end-to-end:
///   1. The currency selector defaults to CRC.
///   2. The preview region stays hidden while CRC is selected (T211).
///   3. Saving 750_000 CRC produces a quotation row that renders only
///      "₡750,000.00 CRC" in Application Details — NO conversion indicator,
///      NO rate-snapshot box, NO legacy-needs-review badge.
///
/// Mirrors the structure of <see cref="ApplicantUsdQuoteE2E"/>; the only
/// material difference is the currency choice and the post-save assertions.
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

        var (appId, itemId, supplierId, supplierName) =
            await CreateApplicationItemAndSupplierAsync(uniqueId);

        var page = new AddQuotationPage(Page);
        await page.GotoAsync(BaseUrl, appId, itemId, supplierId, supplierName);

        // The form defaults to CRC (the catalog's base + first option). Verify before typing.
        await Expect(page.CurrencyControl).ToHaveValueAsync("CRC");

        // T211 — preview must remain hidden while CRC is selected. Fill the price first
        // (which is what the USD path uses to trigger the preview) and confirm the CRC
        // short-circuit on the JS side keeps the region hidden.
        await page.PriceInput.FillAsync("750000");
        await page.PriceInput.BlurAsync();
        await Expect(page.ConversionPreview).ToBeHiddenAsync();

        // Re-select CRC explicitly to exercise the change handler's hide path.
        await page.SetCurrencyAsync("CRC");
        await Expect(page.ConversionPreview).ToBeHiddenAsync();

        await page.ValidUntilInput.FillAsync("2027-12-31");
        await page.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await page.SubmitAsync();

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

    /// <summary>
    /// Drives the UI through "register/login + create application + add item + save
    /// a placeholder supplier with one quotation". Returns the persisted ids plus the
    /// supplier's display name so the test can navigate back to the Add-Quotation
    /// surface to test our specific code path.
    ///
    /// Mirrors <see cref="ApplicantUsdQuoteE2E.CreateApplicationItemAndSupplierAsync"/>;
    /// the seed quotation here is also CRC (the most natural neighbor for a CRC golden-path).
    /// </summary>
    private async Task<(int AppId, int ItemId, int SupplierId, string SupplierName)>
        CreateApplicationItemAndSupplierAsync(string uniqueId)
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

        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();

        var supplierLegalId = $"SUP-CRC-{uniqueId}";
        var supplierName = $"CRC Supplier {uniqueId}";
        var supplierPage = new SupplierPage(Page);
        var outcome = await supplierPage.SearchByLegalIdAsync(supplierLegalId);
        Assert.That(outcome, Is.EqualTo("Empty"));
        await supplierPage.FillNewSupplierFormAsync(
            name: supplierName,
            branchName: "Sede principal",
            province: "San Jose");
        await supplierPage.FillQuotationFieldsAsync(
            price: 1m, validUntil: "2027-12-31", filePath: _testFilePath, currency: "CRC");
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        var (itemId, supplierId) = await ResolveItemAndSupplierIdsAsync(appId);

        // Removing the seed quotation so we can re-add via the Quotation/Add surface
        // (the (item, supplier) UNIQUE constraint forbids two quotations from the
        // same supplier on the same item).
        await DeleteQuotationAsync(itemId, supplierId);

        return (appId, itemId, supplierId, supplierName);
    }

    private async Task<(int ItemId, int SupplierId)> ResolveItemAndSupplierIdsAsync(int appId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1 i.Id AS ItemId, q.SupplierId
            FROM dbo.Items i
            INNER JOIN dbo.Quotations q ON q.ItemId = i.Id
            WHERE i.ApplicationId = @AppId
            ORDER BY i.Id DESC, q.Id DESC;";
        cmd.Parameters.AddWithValue("@AppId", appId);

        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "Could not resolve seeded item/supplier pair.");
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task DeleteQuotationAsync(int itemId, int supplierId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM dbo.Quotations
            WHERE ItemId = @ItemId AND SupplierId = @SupplierId;";
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@SupplierId", supplierId);
        await cmd.ExecuteNonQueryAsync();
    }
}
