using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 015 / US3 / T302 — administrator manages the currency catalog at
/// <c>/Admin/AdminCurrencies</c>. Three scenarios:
///   1. Admin sees both CRC + USD rows on first load.
///   2. Admin disables USD, then re-enables USD; status badge flips both ways.
///   3. Admin attempts to disable CRC and sees the FR-002 inline error
///      (the toggle button is suppressed; the row renders the locked notice).
/// </summary>
public class AdminCurrencyConfigE2E : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    private async Task<string> SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"curr_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "Currency", "Admin", $"CADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);
        return adminEmail;
    }

    /// <summary>
    /// Reset USD to enabled before each test so the parallel-fixture state from
    /// prior tests doesn't bleed in. The seed inserts USD enabled; this is
    /// idempotent for the GoldenPath case.
    /// </summary>
    private async Task ResetUsdToEnabledAsync()
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Currencies SET IsEnabled = 1 WHERE Code = 'USD';";
        await cmd.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task AdminCurrencies_ListShowsCrcAndUsd()
    {
        await SignInAsAdminAsync();
        await ResetUsdToEnabledAsync();

        var page = new AdminCurrenciesPage(Page);
        await page.GoToAsync(BaseUrl);

        await Expect(page.RowFor("CRC")).ToBeVisibleAsync();
        await Expect(page.RowFor("USD")).ToBeVisibleAsync();
        await Expect(page.BaseLockedNotice("CRC")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminCurrencies_Usd_DisableThenEnable_RoundTrips()
    {
        await SignInAsAdminAsync();
        await ResetUsdToEnabledAsync();

        var page = new AdminCurrenciesPage(Page);
        await page.GoToAsync(BaseUrl);

        // Disable USD.
        await Expect(page.DisableButton("USD")).ToBeVisibleAsync();
        await page.ClickDisableAsync("USD");
        await Expect(page.StatusBadge("USD", enabled: false)).ToBeVisibleAsync();

        // Re-enable USD.
        await Expect(page.EnableButton("USD")).ToBeVisibleAsync();
        await page.ClickEnableAsync("USD");
        await Expect(page.StatusBadge("USD", enabled: true)).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminCurrencies_Crc_DisableButtonHidden_ShowsBaseLockedNotice()
    {
        await SignInAsAdminAsync();
        await ResetUsdToEnabledAsync();

        var page = new AdminCurrenciesPage(Page);
        await page.GoToAsync(BaseUrl);

        // The toggle button is suppressed for the base currency; the row renders
        // the FR-002 locked-notice instead. This is the UX surface for the
        // FR-002 invariant — the user cannot even attempt the action.
        Assert.That(await page.DisableButton("CRC").CountAsync(), Is.EqualTo(0),
            "CRC must not expose a Disable button.");
        await Expect(page.BaseLockedNotice("CRC")).ToBeVisibleAsync();
        await Expect(page.BaseLockedNotice("CRC"))
            .ToContainTextAsync(new Regex("CRC es la moneda base del sistema"));
    }
}
