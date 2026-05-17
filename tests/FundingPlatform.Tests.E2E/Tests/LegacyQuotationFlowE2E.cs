using System.Globalization;
using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US6 / T602 — administrator clears a legacy <c>LegacyNeedsReview</c>
/// flag by attaching a historical exchange rate to a pre-spec-015 USD quotation.
/// Scenario:
///   1. Seed: register an applicant + admin via the existing helpers; create an
///      Application with one Item + Supplier + a single CRC quotation through
///      the UI (so the FK chain is intact).
///   2. Mutate: via direct SQL, retag that quotation as legacy USD with
///      <c>LegacyNeedsReview = 1</c> and the snapshot fields cleared. This
///      simulates exactly what the post-deploy migration produces on a real
///      upgrade.
///   3. Publish a USD↔CRC rate via the admin UI.
///   4. Navigate to <c>/Admin/LegacyQuotations</c>, see the row, pick the
///      rate, submit the attach form.
///   5. Assert the row disappears from the queue, the success banner shows,
///      and the Application Details surface now renders the conversion data
///      (no legacy badge, snapshot subblock present).
/// </summary>
public class LegacyQuotationFlowE2E : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"legacy-quote-{Guid.NewGuid():N}.pdf");
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
    public async Task LegacyFlow_AdminAttachesRate_FlagClears_RowDisappearsFromQueue()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"legacy_admin_{unique}@example.com";
        var applicantEmail = $"legacy_app_{unique}@example.com";

        // Wipe shared-fixture rate state so the picker exposes only the rate we publish below.
        await DeleteAllUsdCrcRatesAsync();

        // 1. Register admin + applicant.
        await RegisterUserAsync(Page, adminEmail, Password, "Legacy", "Admin", $"LADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");

        await RegisterUserAsync(Page, applicantEmail, Password, "Legacy", "Applicant", $"LAPP-{unique}");
        await LoginAsync(Page, applicantEmail, Password);

        // 2. Drive the UI to create an Application + Item + supplier with a single quotation.
        var appPage = new PageObjects.ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new PageObjects.ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Legacy Item", 0, "Specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var addSupplierLink = Page.Locator($"a:has-text('{Constants.UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();

        var supplierLegalId = $"SUP-LEG-{unique}";
        var supplierName = $"Legacy Supplier {unique}";
        var supplierPage = new PageObjects.SupplierPage(Page);
        var outcome = await supplierPage.SearchByLegalIdAsync(supplierLegalId);
        Assert.That(outcome, Is.EqualTo("Empty"));
        await supplierPage.FillNewSupplierFormAsync(
            name: supplierName,
            branchName: "Sede principal",
            province: "San Jose");
        await supplierPage.FillQuotationFieldsAsync(
            price: 1m, validUntil: "2027-12-31", filePath: _testFilePath, currency: "CRC");
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // 3. SQL: morph that CRC quotation into a legacy USD one. Mirrors exactly
        //    what the post-deploy migration produces for a real upgrade.
        var (itemId, quotationId) = await MakeQuotationLegacyUsdAsync(appId, price: 1000m);

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // 4. Admin logs in, publishes a USD rate.
        await LoginAsync(Page, adminEmail, Password);
        var createPage = new AdminExchangeRateCreatePage(Page);
        await Page.GotoAsync($"{BaseUrl}/Admin/ExchangeRates/Create");
        var localStamp = DateTime.Now.AddMinutes(-2)
            .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        await createPage.FillAsync(source: "USD", target: "CRC",
            buy: "520", sell: "525", effectiveLocal: localStamp);
        await createPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ExchangeRates(\\?.*)?$"));

        // 5. Navigate to the legacy queue, see the row, attach the rate.
        var legacyPage = new AdminLegacyQuotationsPage(Page);
        await legacyPage.GoToAsync(BaseUrl);
        await Expect(legacyPage.Table).ToBeVisibleAsync();
        await Expect(legacyPage.RowFor(quotationId)).ToBeVisibleAsync();

        // Pick the (only) rate option (the placeholder option's value is empty).
        var select = legacyPage.RateSelect(quotationId);
        var nonEmptyOption = select.Locator("option[value]:not([value=''])").First;
        var rateId = await nonEmptyOption.GetAttributeAsync("value");
        Assert.That(rateId, Is.Not.Null.And.Not.Empty);
        await select.SelectOptionAsync(rateId!);
        await legacyPage.AttachButton(quotationId).ClickAsync();

        // 6. Row disappears from the queue + success banner shows.
        await Expect(legacyPage.SuccessBanner).ToBeVisibleAsync();
        Assert.That(await legacyPage.RowFor(quotationId).CountAsync(), Is.EqualTo(0),
            "Attached quotation must not appear in the legacy queue anymore.");

        // 7. Switch back to the applicant — Application/Details is gated to the
        //    applicant who owns the row, not the admin who just resolved it.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, applicantEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");
        var legacyBadge = Page.Locator($"[data-testid=quotation-row][data-quotation-id='{quotationId}'] [data-testid=legacy-needs-review]");
        Assert.That(await legacyBadge.CountAsync(), Is.EqualTo(0),
            "Legacy-needs-review badge must be gone after attach.");

        var snapshot = Page.Locator($"[data-testid=quotation-row][data-quotation-id='{quotationId}'] [data-testid=quotation-rate-snapshot]");
        await Expect(snapshot).ToBeVisibleAsync();
        await Expect(snapshot).ToContainTextAsync(new Regex("520"));
    }

    /// <summary>
    /// Mutates the seeded CRC quotation on the application into a legacy USD row
    /// (LegacyNeedsReview=1, snapshot NULL, ConvertedCrcAmount NULL). The
    /// CK_Quotations_NonCrcRequiresSnapshot constraint allows non-CRC rows when
    /// LegacyNeedsReview = 1, so this state is exactly what the post-deploy
    /// migration produces for a pre-spec-015 row.
    /// </summary>
    private async Task<(int ItemId, int QuotationId)> MakeQuotationLegacyUsdAsync(int appId, decimal price)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DECLARE @ItemId INT = (SELECT TOP 1 Id FROM dbo.Items WHERE ApplicationId = @AppId ORDER BY Id);
            DECLARE @QuotationId INT = (
                SELECT TOP 1 q.Id FROM dbo.Quotations q WHERE q.ItemId = @ItemId ORDER BY q.Id);

            UPDATE dbo.Quotations
               SET Currency = 'USD',
                   Price = @Price,
                   ConvertedCrcAmount = NULL,
                   SnapshotRateId = NULL,
                   SnapshotRateValue = NULL,
                   SnapshotRateType = NULL,
                   SnapshotEffectiveAtUtc = NULL,
                   LegacyNeedsReview = 1
             WHERE Id = @QuotationId;

            SELECT @ItemId AS ItemId, @QuotationId AS QuotationId;";
        cmd.Parameters.AddWithValue("@AppId", appId);
        cmd.Parameters.AddWithValue("@Price", price);

        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task DeleteAllUsdCrcRatesAsync()
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
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
