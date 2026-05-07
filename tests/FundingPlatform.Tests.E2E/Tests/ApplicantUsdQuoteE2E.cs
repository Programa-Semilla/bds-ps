using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US1 — applicant creates a USD quotation with a deterministic CRC
/// conversion. Two scenarios:
///   1. Golden path: a USD↔CRC rate is published, the applicant picks USD on the
///      Add form, sees the live preview update server-side, saves, and Application
///      Details renders both the original USD price and the converted CRC amount.
///   2. Failure path (FR-018): no rate is published; the applicant tries to save
///      a USD quotation and sees the literal Spanish FR-018 message inline on the
///      form ("No hay tipo de cambio de referencia configurado. Contacte a un
///      administrador.").
/// </summary>
public class ApplicantUsdQuoteE2E : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"usd-quote-{Guid.NewGuid():N}.pdf");
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
    public async Task UsdQuotation_GoldenPath_PreviewSaves_ApplicationDetailsShowsBothCurrencies()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"usd_app_{uniqueId}@example.com";
        const string password = "Test123!";

        await PublishUsdRateAsync(buy: 520m, sell: 525m);

        await RegisterUserAsync(Page, email, password, "USD", "Applicant", $"USD-{uniqueId}");
        await LoginAsync(Page, email, password);

        var (appId, itemId, supplierId, supplierName) =
            await CreateApplicationItemAndSupplierAsync(uniqueId);

        var page = new AddQuotationPage(Page);
        await page.GotoAsync(BaseUrl, appId, itemId, supplierId, supplierName);

        await page.PriceInput.FillAsync("1000");
        await page.SetCurrencyAsync("USD");

        // Conversion preview region must become visible with the converted CRC.
        // Note: Intl.NumberFormat for es-CR uses U+00A0 (non-breaking space) as
        // the thousands separator; \s in .NET regex matches it, plain ' ' does not.
        await Expect(page.ConversionPreview).ToBeVisibleAsync();
        await Expect(page.PreviewAmount).ToContainTextAsync(new Regex(@"520[\.,\s]?000"));
        await Expect(page.PreviewRate).ToContainTextAsync(new Regex("USD"));
        await Expect(page.PreviewRate).ToContainTextAsync(new Regex("520"));

        await page.ValidUntilInput.FillAsync("2027-12-31");
        await page.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await page.SubmitAsync();

        // Saved → redirected back to Application Details.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Detail page shows the converted CRC + the original USD amount.
        var quotationRow = Page.Locator("[data-testid=quotation-row]").First;
        await Expect(quotationRow).ToBeVisibleAsync();
        await Expect(quotationRow).ToContainTextAsync(new Regex(@"1[\.,\s]?000"));
        await Expect(quotationRow).ToContainTextAsync(new Regex("USD"));
        await Expect(quotationRow).ToContainTextAsync(new Regex(@"520[\.,\s]?000"));
        await Expect(quotationRow).ToContainTextAsync(new Regex("CRC"));

        var snapshotBox = Page.Locator("[data-testid=quotation-rate-snapshot]").First;
        await Expect(snapshotBox).ToBeVisibleAsync();
        await Expect(snapshotBox).ToContainTextAsync(new Regex("520"));
    }

    [Test]
    public async Task UsdQuotation_NoRatePublished_BlocksSaveWithFr018Message()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"usd_norate_{uniqueId}@example.com";
        const string password = "Test123!";

        // Deliberately NOT publishing a rate. Wipe any rates from prior tests sharing the fixture.
        await DeleteAllUsdCrcRatesAsync();

        await RegisterUserAsync(Page, email, password, "NoRate", "Applicant", $"USDN-{uniqueId}");
        await LoginAsync(Page, email, password);

        var (appId, itemId, supplierId, supplierName) =
            await CreateApplicationItemAndSupplierAsync(uniqueId);

        var page = new AddQuotationPage(Page);
        await page.GotoAsync(BaseUrl, appId, itemId, supplierId, supplierName);

        await page.PriceInput.FillAsync("1000");
        await page.SetCurrencyAsync("USD");
        await page.ValidUntilInput.FillAsync("2027-12-31");
        await page.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await page.SubmitAsync();

        // Form re-renders with the literal Spanish FR-018 message.
        await Expect(Page).ToHaveURLAsync(new Regex("/Quotation/Add"));
        var summary = Page.Locator(".text-danger");
        await Expect(summary.First).ToContainTextAsync(
            new Regex("No hay tipo de cambio de referencia configurado"));
    }

    /// <summary>
    /// Drives the UI through "register/login + create application + add item + save
    /// a placeholder supplier with one quotation". Returns the persisted ids plus the
    /// supplier's display name so the test can navigate back to the Add-Quotation
    /// surface to test our specific code path.
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
        await itemPage.AddItemAsync(appId, "USD Test Item", 0, "Specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Add the first supplier with a CRC seed quotation through the existing supplier flow,
        // so the Application has a supplier on the Item we can target via the simpler
        // Quotation/Add surface for the second quotation.
        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();

        var supplierLegalId = $"SUP-USD-{uniqueId}";
        var supplierName = $"USD Supplier {uniqueId}";
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

    /// <summary>
    /// Inserts a USD↔CRC <see cref="FundingPlatform.Domain.Entities.ExchangeRate"/> row directly
    /// via SQL, bypassing the (not-yet-shipped) admin UI. Idempotent — wipes any prior rates so
    /// the GoldenPath test can start from a known state every run.
    /// </summary>
    private async Task PublishUsdRateAsync(decimal buy, decimal sell)
    {
        await DeleteAllUsdCrcRatesAsync();

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            -- CreatedByUserId is FK'd to dbo.AspNetUsers; pick any existing user
            -- (the system sentinel admin is always present per spec 009 seed).
            DECLARE @CreatedById NVARCHAR(450) = (SELECT TOP 1 [Id] FROM dbo.AspNetUsers
                                                  WHERE [IsSystemSentinel] = 1);
            IF @CreatedById IS NULL SET @CreatedById = (SELECT TOP 1 [Id] FROM dbo.AspNetUsers);

            INSERT INTO dbo.ExchangeRates
                (Id, SourceCurrencyCode, TargetCurrencyCode, BuyRate, SellRate,
                 EffectiveAtUtc, CreatedByUserId, CreatedAtUtc, IsUsed)
            VALUES
                (@Id, 'USD', 'CRC', @Buy, @Sell, SYSUTCDATETIME(), @CreatedById, SYSUTCDATETIME(), 0);";
        cmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@Buy", buy);
        cmd.Parameters.AddWithValue("@Sell", sell);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task DeleteAllUsdCrcRatesAsync()
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            -- Clear snapshot FK references first, then the rates. Mark any quotations
            -- whose snapshot we're about to dangle as legacy-needs-review so the
            -- CK_Quotations_NonCrcRequiresSnapshot constraint stays satisfied.
            UPDATE dbo.Quotations
               SET SnapshotRateId = NULL,
                   SnapshotRateValue = NULL,
                   SnapshotRateType = NULL,
                   SnapshotEffectiveAtUtc = NULL,
                   LegacyNeedsReview = 1
             WHERE SnapshotRateId IS NOT NULL AND Currency <> 'CRC';
            DELETE FROM dbo.ExchangeRates WHERE SourceCurrencyCode = 'USD' AND TargetCurrencyCode = 'CRC';";
        await cmd.ExecuteNonQueryAsync();
    }
}
