// Spec 031 / US2 (T021) — searchable entity-reference edit form.
//
// /Admin/Processes/Create has a required Fund <select>. We SQL-seed 8 extra Funds
// (9 total > 7) so it enhances into a combobox, then assert:
//   - search + select commits the SAME id the plain dropdown would, and the
//     created Process persists that Fund (SC-002),
//   - a no-match (uncommitted) submit leaves the required Fund empty and fails
//     server validation exactly as the plain dropdown does (US2 scenario 2).

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.SearchableDropdowns;

public class EditFormSearchTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private string _fundPrefix = string.Empty;

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"s031us2_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "S031", "Admin", $"S32A-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [TearDown]
    public async Task CleanUpSeededFunds()
    {
        if (!string.IsNullOrEmpty(_fundPrefix))
        {
            await SearchableSeed.RemoveFundsAsync(ConnectionString, _fundPrefix);
        }
    }

    // Backstop: purge any Spec031-prefixed throwaway funds if a per-test teardown
    // was skipped (host crash), so they can't pollute the shared fixture.
    [OneTimeTearDown]
    public async Task PurgeSpec031Funds() =>
        await SearchableSeed.RemoveFundsAsync(ConnectionString, "Spec031");

    [Test]
    public async Task ProcessCreate_FundSearch_CommitsAndPersistsSelectedFund()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        _fundPrefix = $"Spec031US2 {suffix}";
        await SignInAsAdminAsync(suffix);
        await SearchableSeed.SeedFundsAsync(ConnectionString, _fundPrefix, 8);

        await Page.GotoAsync($"{BaseUrl}/Admin/Processes/Create");

        var fund = new SearchableSelect(Page, "admin-process-fund-select");
        await Expect(fund.Input).ToBeVisibleAsync();

        var expectedValue = await fund.NativeSelect.Locator("option")
            .Filter(new LocatorFilterOptions { HasText = "Fondo General" })
            .First.GetAttributeAsync("value");

        await fund.SelectSearchableAsync("Fondo General");
        Assert.That(await fund.CommittedValueAsync(), Is.EqualTo(expectedValue),
            "Combobox must commit the same id the plain dropdown would (SC-002).");

        var processName = $"Proc031 {suffix}";
        await Page.Locator("[data-testid=admin-process-name-input]").FillAsync(processName);
        await Page.Locator("[data-testid=admin-process-create-submit]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Processes(\\?.*)?$"));
        var row = Page.Locator("tr[data-testid^=\"admin-process-row-\"]")
            .Filter(new LocatorFilterOptions { HasText = processName });
        await Expect(row).ToContainTextAsync("Fondo General");
    }

    [Test]
    public async Task ProcessCreate_RequiredFundLeftEmpty_FailsServerValidation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        _fundPrefix = $"Spec031US2e {suffix}";
        await SignInAsAdminAsync(suffix);
        await SearchableSeed.SeedFundsAsync(ConnectionString, _fundPrefix, 8);

        await Page.GotoAsync($"{BaseUrl}/Admin/Processes/Create");

        var fund = new SearchableSelect(Page, "admin-process-fund-select");
        await Expect(fund.Input).ToBeVisibleAsync();

        // Type a fragment that matches nothing and never commit it.
        await fund.FilterAsync("zzznomatch");
        await Expect(fund.EmptyState).ToBeVisibleAsync();

        var nameInput = Page.Locator("[data-testid=admin-process-name-input]");
        await nameInput.FillAsync($"Proc031e {suffix}"); // also blurs the combobox → reverts to empty
        Assert.That(await fund.CommittedValueAsync(), Is.EqualTo(string.Empty),
            "A no-match query must never fabricate a Fund value (FR-003).");

        await Page.Locator("[data-testid=admin-process-create-submit]").ClickAsync();

        // Server re-renders Create with the required-Fund validation error.
        await Expect(Page.Locator("[data-testid=admin-process-fund-error]")).Not.ToBeEmptyAsync();
    }
}
