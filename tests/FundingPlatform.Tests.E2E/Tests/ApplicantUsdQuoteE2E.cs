using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US1 — applicant creates a USD quotation through the real applicant
/// path the product actually exposes:
///   Application/Details → "Agregar proveedor" → SupplierPage (legal-ID lookup +
///   new-supplier form) → multi-currency quote section (dropdown + live CRC
///   preview) → submit → land back on Application/Details with the saved row.
///
/// Two scenarios:
///   1. Golden path: a USD↔CRC rate is published, the applicant picks USD on the
///      Add form, sees the preview update server-side via the AJAX Convert call,
///      saves, and Application/Details renders both the original USD price and
///      the converted CRC amount.
///   2. Failure path (FR-018): no rate is published; the applicant tries to save
///      a USD quotation and sees the literal Spanish FR-018 message inline on
///      the form ("No hay tipo de cambio de referencia configurado…").
///
/// History note: the previous version of this test navigated directly to a now-
/// orphaned <c>/Application/{appId}/Item/{itemId}/Quotation/Add</c> URL — a route
/// no UI surface ever linked to. That bypassed the actual applicant journey and
/// tested an isolated controller. The current test exercises the path the
/// applicant clicks through in the real product.
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

        await CreateApplicationAndItemAsync();
        await ClickAddSupplierOnItemRowAsync();

        var supplierPage = new SupplierPage(Page);
        var supplierLegalId = $"SUP-USD-{uniqueId}";
        var supplierName = $"USD Supplier {uniqueId}";

        Assert.That(await supplierPage.SearchByLegalIdAsync(supplierLegalId), Is.EqualTo("Empty"));

        await supplierPage.FillNewSupplierFormAsync(
            name: supplierName,
            branchName: "Sede principal",
            province: "San Jose");

        await supplierPage.PriceInput.FillAsync("1000");
        await supplierPage.SetCurrencyAsync("USD");

        // The conversion preview must come up server-rendered with the CRC amount.
        // Intl.NumberFormat for es-CR uses U+00A0 (non-breaking space) as the
        // thousands separator; \s in .NET regex matches it, plain ' ' does not.
        await Expect(supplierPage.ConversionPreview).ToBeVisibleAsync();
        await Expect(supplierPage.PreviewAmount).ToContainTextAsync(new Regex(@"520[\.,\s]?000"));
        await Expect(supplierPage.PreviewRate).ToContainTextAsync(new Regex("USD"));
        await Expect(supplierPage.PreviewRate).ToContainTextAsync(new Regex("520"));

        await supplierPage.ValidUntilInput.FillAsync("2027-12-31");
        await supplierPage.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await supplierPage.SubmitAsync();

        // Saved → redirected back to the draft editor.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Editor shows the converted CRC + the original USD amount.
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

        await CreateApplicationAndItemAsync();
        await ClickAddSupplierOnItemRowAsync();

        var supplierPage = new SupplierPage(Page);
        var supplierLegalId = $"SUP-USDN-{uniqueId}";
        var supplierName = $"USD NoRate Supplier {uniqueId}";

        Assert.That(await supplierPage.SearchByLegalIdAsync(supplierLegalId), Is.EqualTo("Empty"));

        await supplierPage.FillNewSupplierFormAsync(
            name: supplierName,
            branchName: "Sede principal",
            province: "San Jose");

        await supplierPage.PriceInput.FillAsync("1000");
        await supplierPage.SetCurrencyAsync("USD");
        await supplierPage.ValidUntilInput.FillAsync("2027-12-31");
        await supplierPage.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await supplierPage.SubmitAsync();

        // Form re-renders on the same Supplier/Add URL with the FR-018 message.
        // Pin the assertion to the Currency field's validation span — the
        // controller adds the FR-018 model error against nameof(model.Currency),
        // so the rendered <span data-valmsg-for="Currency"> is the one and only
        // deterministic anchor. Asserting on .text-danger more broadly trips
        // strict-mode (validation summary + field span both carry that class).
        await Expect(Page).ToHaveURLAsync(new Regex("/Supplier/Add"));
        var currencyError = Page.Locator("[data-valmsg-for=Currency]");
        await Expect(currencyError).ToContainTextAsync(
            new Regex("No hay tipo de cambio de referencia configurado"));
    }

    /// <summary>
    /// Drives the real applicant journey up to (but not including) the click on
    /// "Agregar proveedor": create application → add one item → land on
    /// Application/Details with the item row visible.
    /// </summary>
    private async Task CreateApplicationAndItemAsync()
    {
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "USD Test Item", 0, "Specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    private async Task ClickAddSupplierOnItemRowAsync()
    {
        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Supplier/Add"));
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
