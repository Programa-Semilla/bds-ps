using System.Globalization;
using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US3 / T303 — administrator manages reference rates at
/// <c>/Admin/ExchangeRates</c>. Scenarios:
///   1. Create a valid USD↔CRC rate (520/525) and see it appear in the
///      history list as the active rate.
///   2. Submitting BuyRate=0 surfaces the validation error inline.
///   3. Submitting a duplicate-timestamp rate triggers the FR-007 message
///      (when the SQL unique index UQ_ExchangeRates_PairAt fires).
///   4. Submitting a future-dated EffectiveAt rejects with FR-007a.
///   5. The newest published rate is highlighted as active.
/// </summary>
public class AdminExchangeRateE2E : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    private async Task<string> SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"rate_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "Rate", "Admin", $"RADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);
        return adminEmail;
    }

    /// <summary>
    /// Wipe rates + clear any quotation snapshot FK references so each test
    /// starts on a known state. Mirrors the helper in ApplicantUsdQuoteE2E.
    /// </summary>
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

    /// <summary>
    /// datetime-local field expects ISO-8601 without timezone. The form maps to
    /// EffectiveAtLocal in the controller, which converts to UTC server-side.
    /// </summary>
    private static string MinutesAgoLocal(int minutes)
        => DateTime.Now.AddMinutes(-minutes).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    [Test]
    public async Task AdminExchangeRates_CreateValidRate_AppearsInHistoryAndIsActive()
    {
        await SignInAsAdminAsync();
        await DeleteAllUsdCrcRatesAsync();

        var createPage = new AdminExchangeRateCreatePage(Page);
        await Page.GotoAsync($"{BaseUrl}/Admin/ExchangeRates/Create");

        await createPage.FillAsync(
            source: "USD", target: "CRC",
            buy: "520", sell: "525",
            effectiveLocal: MinutesAgoLocal(2));
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ExchangeRates(\\?.*)?$"));

        var listPage = new AdminExchangeRatesPage(Page);
        await Expect(listPage.Table).ToBeVisibleAsync();
        Assert.That(await listPage.AnyRow.CountAsync(), Is.GreaterThanOrEqualTo(1));
        await Expect(listPage.ActiveBadges.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminExchangeRates_ZeroBuy_RejectedInline()
    {
        await SignInAsAdminAsync();
        await DeleteAllUsdCrcRatesAsync();

        var createPage = new AdminExchangeRateCreatePage(Page);
        await Page.GotoAsync($"{BaseUrl}/Admin/ExchangeRates/Create");

        await createPage.FillAsync(
            source: "USD", target: "CRC",
            buy: "0", sell: "525",
            effectiveLocal: MinutesAgoLocal(2));
        await createPage.SubmitAsync();

        // We must remain on the Create page rendering the inline error.
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ExchangeRates"));
        var summary = Page.Locator(".validation-summary-errors, .field-validation-error");
        await Expect(summary.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminExchangeRates_FutureDated_RejectedWithFr007a()
    {
        await SignInAsAdminAsync();
        await DeleteAllUsdCrcRatesAsync();

        var createPage = new AdminExchangeRateCreatePage(Page);
        await Page.GotoAsync($"{BaseUrl}/Admin/ExchangeRates/Create");

        var future = DateTime.Now.AddHours(1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        await createPage.FillAsync(
            source: "USD", target: "CRC",
            buy: "520", sell: "525",
            effectiveLocal: future);
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ExchangeRates"));
        var summary = Page.Locator(".validation-summary-errors, .field-validation-error");
        await Expect(summary.First).ToContainTextAsync(
            new Regex("no puede tener una fecha de vigencia en el futuro", RegexOptions.IgnoreCase));
    }

    [Test]
    public async Task AdminExchangeRates_DuplicateTimestamp_Rejected()
    {
        await SignInAsAdminAsync();
        await DeleteAllUsdCrcRatesAsync();

        // Insert a rate at a known UTC timestamp. Truncate to minute precision
        // so it round-trips through the datetime-local input (which only has
        // minute precision) and lands back on the same UTC instant.
        var raw = DateTime.UtcNow.AddMinutes(-30);
        var fixedUtc = new DateTime(raw.Year, raw.Month, raw.Day, raw.Hour, raw.Minute, 0, DateTimeKind.Utc);
        using (var conn = new SqlConnection(ConnectionString))
        {
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
                    (@Id, 'USD', 'CRC', 500, 510, @When, @CreatedById, SYSUTCDATETIME(), 0);";
            cmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@When", fixedUtc);
            await cmd.ExecuteNonQueryAsync();
        }

        var createPage = new AdminExchangeRateCreatePage(Page);
        await Page.GotoAsync($"{BaseUrl}/Admin/ExchangeRates/Create");

        // datetime-local value reflects local time. Convert the seeded UTC stamp to local for the form.
        var localStamp = fixedUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        await createPage.FillAsync(
            source: "USD", target: "CRC",
            buy: "520", sell: "525",
            effectiveLocal: localStamp);
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ExchangeRates"));
        var summary = Page.Locator(".validation-summary-errors, .field-validation-error");
        await Expect(summary.First).ToContainTextAsync(
            new Regex("Ya existe un tipo de cambio publicado", RegexOptions.IgnoreCase));
    }

    [Test]
    public async Task AdminExchangeRates_NewestPublishedRow_IsHighlightedActive()
    {
        await SignInAsAdminAsync();
        await DeleteAllUsdCrcRatesAsync();

        // Publish two rates at different times via the UI.
        var createPage = new AdminExchangeRateCreatePage(Page);

        await Page.GotoAsync($"{BaseUrl}/Admin/ExchangeRates/Create");
        await createPage.FillAsync("USD", "CRC", "500", "510", MinutesAgoLocal(120));
        await createPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ExchangeRates(\\?.*)?$"));

        await Page.GotoAsync($"{BaseUrl}/Admin/ExchangeRates/Create");
        await createPage.FillAsync("USD", "CRC", "525", "530", MinutesAgoLocal(5));
        await createPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ExchangeRates(\\?.*)?$"));

        var listPage = new AdminExchangeRatesPage(Page);
        // The newest (525/530) row is the first one and has the active badge.
        var firstRow = listPage.AnyRow.First;
        await Expect(firstRow).ToBeVisibleAsync();
        await Expect(firstRow).ToContainTextAsync(new Regex("525"));
        await Expect(firstRow.Locator("[data-testid=\"rate-active-badge\"]")).ToBeVisibleAsync();
    }
}
