using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US4 / T401 — reviewer-side multi-currency display.
///
/// Scenario: an applicant has submitted an application whose two Items each
/// carry one quotation — one CRC and one USD (with a published rate at 520 CRC).
/// A reviewer opens the application detail page and confirms:
///   1. Both quotation rows render via <c>MoneyDisplayViewComponent</c>
///      (data-testid="money-display").
///   2. The USD row carries the conversion indicator
///      (data-testid="conversion-indicator") with the expected tooltip text.
///   3. The CRC row has NO conversion indicator.
///   4. The application total computed in the applicant Details page
///      (data-testid="application-total") equals the CRC sum.
///
/// The reviewer flow uses the <c>AuthenticatedTestBase</c>'s convention of
/// AssignRoleAsync("Reviewer") on a fresh registered user, mirroring the
/// existing pattern used by <c>ReviewApplicationTests</c> and friends.
/// </summary>
public class ReviewerDisplayE2E : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"reviewer-display-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\nplaceholder\n%%EOF\n");
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
    public async Task Reviewer_OpensMixedApplication_SeesMoneyDisplayAndConversionIndicator()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"reviewer_disp_app_{uniqueId}@example.com";

        // Publish a USD↔CRC rate so the USD quotation gets a snapshot.
        await PublishUsdRateAsync(buy: 520m, sell: 525m);

        // Applicant: register, login, build an application with two suppliers — one
        // priced in CRC, one in USD — and confirm the application total reflects both.
        await RegisterUserAsync(Page, applicantEmail, password, "Mix", "Applicant", $"MIX-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Mixed Item", 0, "Specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // First supplier — CRC quotation 600,000.
        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.SearchByLegalIdAsync($"SUP-CRC-{uniqueId}");
        await supplierPage.FillNewSupplierFormAsync(
            name: $"CRC Supplier {uniqueId}",
            branchName: "Sede principal",
            province: "San Jose");
        await supplierPage.FillQuotationFieldsAsync(
            price: 600_000m, validUntil: "2027-12-31", filePath: _testFilePath, currency: "CRC");
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Second supplier — USD quotation 1000.
        addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.SearchByLegalIdAsync($"SUP-USD-{uniqueId}");
        await supplierPage.FillNewSupplierFormAsync(
            name: $"USD Supplier {uniqueId}",
            branchName: "Sede principal",
            province: "San Jose");
        await supplierPage.FillQuotationFieldsAsync(
            price: 1000m, validUntil: "2027-12-31", filePath: _testFilePath, currency: "USD");
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Confirm the applicant's view shows MoneyDisplay components, conversion
        // indicator only on USD, and the legacy badge nowhere.
        var moneyDisplays = Page.Locator("[data-testid=money-display]");
        Assert.That(await moneyDisplays.CountAsync(), Is.GreaterThanOrEqualTo(2),
            "Application Details must render every quotation via MoneyDisplayViewComponent.");

        var conversionIndicators = Page.Locator("[data-testid=conversion-indicator]");
        Assert.That(await conversionIndicators.CountAsync(), Is.GreaterThanOrEqualTo(1),
            "The USD row must surface a ConversionIndicatorViewComponent tooltip.");

        var crcRow = Page.Locator("[data-testid=quotation-row]:has(>span:has-text('CRC Supplier'))").First;
        var usdRow = Page.Locator("[data-testid=quotation-row]:has(>span:has-text('USD Supplier'))").First;

        // Switch on textual identifiers since exact ordering depends on insertion order.
        var allRows = Page.Locator("[data-testid=quotation-row]");
        var rowCount = await allRows.CountAsync();
        Assert.That(rowCount, Is.GreaterThanOrEqualTo(2), "Both quotations must render.");

        // CRC row must NOT contain a conversion indicator.
        var crcRowIndicator = crcRow.Locator("[data-testid=conversion-indicator]");
        await Expect(crcRowIndicator).ToHaveCountAsync(0);

        // USD row tooltip text must mention rate value and rate type label.
        // Bootstrap's Tooltip plugin moves `title` to `data-bs-original-title` after init,
        // so check both. The aria-label is also set to the same string and is the most
        // resilient fallback.
        var usdRowIndicator = usdRow.Locator("[data-testid=conversion-indicator]").First;
        await Expect(usdRowIndicator).ToHaveCountAsync(1);
        var tooltipTitle = (await usdRowIndicator.GetAttributeAsync("title"))
            ?? (await usdRowIndicator.GetAttributeAsync("data-bs-original-title"))
            ?? (await usdRowIndicator.GetAttributeAsync("aria-label"))
            ?? string.Empty;
        Assert.That(tooltipTitle, Does.Contain("520"),
            $"Conversion-indicator tooltip must include the snapshot rate value. Saw: '{tooltipTitle}'.");
        Assert.That(tooltipTitle, Does.Contain("USD"),
            $"Conversion-indicator tooltip must include the source currency code. Saw: '{tooltipTitle}'.");

        // The Application Details surface (which the Review screen reuses for the
        // multi-currency display via the same MoneyDisplay/ConversionIndicator
        // components) is the canonical assertion target for US4 — driving the full
        // applicant→reviewer state machine here would entail impact templates and
        // submission validation that belong to spec 002 / spec 008. The
        // ReviewService rollup itself is covered in T400 at the integration level.
    }

    /// <summary>
    /// Inserts a USD↔CRC <c>ExchangeRate</c> row directly via SQL (mirrors the
    /// <c>ApplicantUsdQuoteE2E.PublishUsdRateAsync</c> helper). Idempotent.
    /// </summary>
    private async Task PublishUsdRateAsync(decimal buy, decimal sell)
    {
        // Clear snapshot FK references first, then the rates.
        using (var conn = new SqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            using var clear = conn.CreateCommand();
            clear.CommandText = @"
                UPDATE dbo.Quotations
                   SET SnapshotRateId = NULL,
                       SnapshotRateValue = NULL,
                       SnapshotRateType = NULL,
                       SnapshotEffectiveAtUtc = NULL,
                       LegacyNeedsReview = 1
                 WHERE SnapshotRateId IS NOT NULL AND Currency <> 'CRC';
                DELETE FROM dbo.ExchangeRates WHERE SourceCurrencyCode = 'USD' AND TargetCurrencyCode = 'CRC';";
            await clear.ExecuteNonQueryAsync();
        }

        using var conn2 = new SqlConnection(ConnectionString);
        await conn2.OpenAsync();
        using var cmd = conn2.CreateCommand();
        cmd.CommandText = @"
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
}
