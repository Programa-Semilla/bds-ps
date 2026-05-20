using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Application;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Application;

/// <summary>
/// Spec 023 / US3 — applicant changes a quotation's currency from CRC to USD.
/// The system snapshots a fresh USD→CRC rate, marks the consumed rate
/// <c>IsUsed = true</c> (spec 015 / FR-008), recomputes the CRC-equivalent,
/// and silently invalidates the <c>ComparisonArtifact</c> cache for the Item
/// (FR-009).
/// </summary>
public class QuotationEditCurrencyTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"currency-edit-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\nplaceholder quotation\n%%EOF\n");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    [Test]
    public async Task ChangesCurrencyCrcToUsd_SnapshotFresh_RateMarkedUsed()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"qedit_currency_{uniqueId}@example.com";
        const string password = "Test123!";

        await PublishUsdRateAsync(buy: 520m, sell: 525m);

        await RegisterUserAsync(Page, email, password, "QEdit", "Currency", $"QEC-{uniqueId}");
        await LoginAsync(Page, email, password);

        var seeded = await SeedDraftWithCrcQuotationAsync(uniqueId);

        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{seeded.AppId}");
        await QuotationEditPage.EditButtonFor(Page, seeded.QuotationId).ClickAsync();

        var editPage = new QuotationEditPage(Page);
        await editPage.SetCurrencyAsync("USD");
        await editPage.SubmitAsync();
        await editPage.WaitForRedirectToApplicationEditAsync(seeded.AppId);

        // Verify the snapshot persisted + rate flipped IsUsed.
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT q.[Currency], q.[ConvertedCrcAmount], q.[SnapshotRateValue], q.[SnapshotRateId],
                   r.[IsUsed]
              FROM dbo.Quotations q
         LEFT JOIN dbo.ExchangeRates r ON r.[Id] = q.[SnapshotRateId]
             WHERE q.[Id] = @QuotationId;";
        cmd.Parameters.AddWithValue("@QuotationId", seeded.QuotationId);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That((string)reader["Currency"], Is.EqualTo("USD"));
        Assert.That(reader["SnapshotRateId"], Is.Not.EqualTo(DBNull.Value),
            "A fresh snapshot rate row must be attached.");
        Assert.That((decimal)reader["SnapshotRateValue"], Is.EqualTo(520m));
        // ConvertedCrcAmount = 1500 (original CRC seed price) * 520 = 780_000.
        Assert.That((decimal)reader["ConvertedCrcAmount"], Is.EqualTo(780_000m));
        Assert.That((bool)reader["IsUsed"], Is.True,
            "Consumed rate must be marked IsUsed (spec 015 FR-008).");
    }

    [Test]
    public async Task InvalidatesComparisonCacheOnEdit()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"qedit_cache_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "QEdit", "Cache", $"QECC-{uniqueId}");
        await LoginAsync(Page, email, password);

        var seeded = await SeedDraftWithCrcQuotationAsync(uniqueId);

        // Pre-seed a ComparisonArtifact for the Item so we can verify it is gone
        // after the Edit POST. Hash + versions are arbitrary 64-hex placeholders
        // satisfying ComparisonArtifactConfiguration constraints.
        await SeedComparisonArtifactAsync(seeded.ItemId);

        // Sanity — the row is present pre-edit.
        Assert.That(await CountArtifactsForItemAsync(seeded.ItemId), Is.EqualTo(1));

        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{seeded.AppId}");
        await QuotationEditPage.EditButtonFor(Page, seeded.QuotationId).ClickAsync();

        var editPage = new QuotationEditPage(Page);
        await editPage.PriceInput.FillAsync("1750"); // any non-idempotent change suffices
        await editPage.SubmitAsync();
        await editPage.WaitForRedirectToApplicationEditAsync(seeded.AppId);

        Assert.That(await CountArtifactsForItemAsync(seeded.ItemId), Is.EqualTo(0),
            "FR-009 — successful Edit must silently invalidate the ComparisonArtifact for the Item.");
    }

    private sealed record Seeded(int AppId, int ItemId, int QuotationId);

    private async Task<Seeded> SeedDraftWithCrcQuotationAsync(string uniqueId)
    {
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync($"Currency Edit Co {uniqueId}");
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, $"Server {uniqueId}", 0, "specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Supplier/Add"));

        var supplierPage = new SupplierPage(Page);
        var supplierLegalId = $"SUP-QCC-{uniqueId}";
        Assert.That(await supplierPage.SearchByLegalIdAsync(supplierLegalId), Is.EqualTo("Empty"));
        await supplierPage.FillNewSupplierFormAsync(
            name: $"Currency Supplier {uniqueId}",
            branchName: "Sede principal",
            province: "San Jose");
        await supplierPage.PriceInput.FillAsync("1500");
        await supplierPage.SetCurrencyAsync("CRC");
        await supplierPage.ValidUntilInput.FillAsync("2027-12-31");
        await supplierPage.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1 q.[Id] AS QuotationId, q.[ItemId]
              FROM dbo.Quotations q
              JOIN dbo.Items i ON i.[Id] = q.[ItemId]
             WHERE i.[ApplicationId] = @AppId
             ORDER BY q.[Id] DESC;";
        cmd.Parameters.AddWithValue("@AppId", appId);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return new Seeded(appId, (int)reader["ItemId"], (int)reader["QuotationId"]);
    }

    private async Task PublishUsdRateAsync(decimal buy, decimal sell)
    {
        await DeleteAllUsdCrcRatesAsync();

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
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

    private async Task SeedComparisonArtifactAsync(int itemId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DECLARE @SeedUser NVARCHAR(450) = (SELECT TOP 1 [Id] FROM dbo.AspNetUsers
                                               WHERE [IsSystemSentinel] = 1);
            IF @SeedUser IS NULL SET @SeedUser = (SELECT TOP 1 [Id] FROM dbo.AspNetUsers);

            INSERT INTO dbo.ComparisonArtifacts
                (ApplicationItemId, JsonContent, InputHash, PromptVersion, SchemaVersion, AiModel,
                 GeneratedAt, GeneratedByUserId, TokenCostInput, TokenCostOutput, LatencyMs)
            VALUES
                (@ItemId, '{}', @Hash, 'v1', 'v1', 'stub', SYSUTCDATETIMEOFFSET(), @SeedUser, 0, 0, 0);";
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@Hash", new string('a', 64));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountArtifactsForItemAsync(int itemId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.ComparisonArtifacts WHERE ApplicationItemId = @ItemId;";
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
