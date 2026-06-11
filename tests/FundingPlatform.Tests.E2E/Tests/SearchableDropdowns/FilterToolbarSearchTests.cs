// Spec 031 / US1 (T012) — searchable admin filter toolbar.
//
// On /Admin/Users the Fondo→Proceso→Grupo cascade filter gets type-to-filter on
// any above-threshold level. The ephemeral seed has 1 Fund, so we SQL-seed 8 more
// (9 total > 7) to push the Fund level into the enhanced combobox, then assert:
//   - the Fund level is a searchable combobox; a below-threshold level stays plain,
//   - typing narrows the list and a no-match shows the empty state,
//   - committing via the combobox sets the SAME value the plain dropdown would,
//   - picking a Fund rebuilds the Process options and the cascade reflects it.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.SearchableDropdowns;

public class FilterToolbarSearchTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private string _fundPrefix = string.Empty;

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"s031us1_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "S031", "Admin", $"S31A-{suffix}");
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

    // Backstop: if a per-test teardown was skipped (host crash), purge any
    // Spec031-prefixed throwaway funds so they can't pollute the shared fixture.
    [OneTimeTearDown]
    public async Task PurgeSpec031Funds() =>
        await SearchableSeed.RemoveFundsAsync(ConnectionString, "Spec031");

    [Test]
    public async Task FundFilterLevel_AboveThreshold_FiltersNarrowsCommitsAndCascades()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        _fundPrefix = $"Spec031US1 {suffix}";
        await SignInAsAdminAsync(suffix);
        await SearchableSeed.SeedFundsAsync(ConnectionString, _fundPrefix, 8);

        await Page.GotoAsync($"{BaseUrl}/Admin/Users");
        // Cascade JS finished building option sets.
        await Page.Locator("[data-testid=\"admin-users-cascade\"][data-ready=\"true\"]").WaitForAsync();

        var fund = new SearchableSelect(Page, "admin-users-cascade-fund");

        // Fund level (9 funds > 7) is enhanced to a combobox.
        await Expect(fund.Input).ToBeVisibleAsync();
        // A below-threshold level (Process: a single process exists) stays plain.
        await Expect(Page.Locator("[data-testid=\"admin-users-cascade-process-search\"]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=\"admin-users-cascade-process\"]")).ToBeVisibleAsync();

        // Typing narrows to the only fund whose label contains "General".
        await fund.FilterAsync("General");
        await Expect(fund.Options).ToHaveCountAsync(1);
        await Expect(fund.Options.First).ToContainTextAsync("Fondo General");

        // A no-match query shows the empty state.
        await fund.FilterAsync("zzzznomatch");
        await Expect(fund.Options).ToHaveCountAsync(0);
        await Expect(fund.EmptyState).ToBeVisibleAsync();

        // Committing via the combobox equals the plain-dropdown value (SC-002).
        var expectedValue = await fund.NativeSelect.Locator("option")
            .Filter(new LocatorFilterOptions { HasText = "Fondo General" })
            .First.GetAttributeAsync("value");
        await fund.SelectSearchableAsync("Fondo General");
        Assert.That(await fund.CommittedValueAsync(), Is.EqualTo(expectedValue));

        // Picking a Fund WITHOUT processes rebuilds Process to empty (cascade reflects it)…
        // Poll (not a one-shot snapshot) so a not-yet-rebuilt list can't false-pass.
        await fund.SelectSearchableAsync($"{_fundPrefix} 03");
        await Expect(Page.Locator("[data-testid=\"admin-users-cascade-process\"] option")
            .Filter(new LocatorFilterOptions { HasText = "Migración inicial" })).ToHaveCountAsync(0);

        // …and re-picking Fondo General brings its process back.
        await fund.SelectSearchableAsync("Fondo General");
        await Expect(Page.Locator("[data-testid=\"admin-users-cascade-process\"] option")
            .Filter(new LocatorFilterOptions { HasText = "Migración inicial" })).ToHaveCountAsync(1);
    }

    [Test]
    public async Task FundFilter_Combobox_HasAriaSemantics_AndCommitsViaKeyboard()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        _fundPrefix = $"Spec031US1a {suffix}";
        await SignInAsAdminAsync(suffix);
        await SearchableSeed.SeedFundsAsync(ConnectionString, _fundPrefix, 8);

        await Page.GotoAsync($"{BaseUrl}/Admin/Users");
        await Page.Locator("[data-testid=\"admin-users-cascade\"][data-ready=\"true\"]").WaitForAsync();

        var fund = new SearchableSelect(Page, "admin-users-cascade-fund");
        await Expect(fund.Input).ToBeVisibleAsync();

        // FR-004 — WAI-ARIA combobox semantics.
        await Expect(fund.Input).ToHaveAttributeAsync("role", "combobox");
        await Expect(fund.Input).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(fund.Input).ToHaveAttributeAsync("aria-controls", new Regex("fl-searchable-list"));

        // Opening sets aria-expanded; the active option is tracked via aria-activedescendant.
        await fund.Input.ClickAsync();
        await Expect(fund.Input).ToHaveAttributeAsync("aria-expanded", "true");

        // FR-004 — Arrow keys move the highlight (the full list is shown, >1 option).
        await fund.Input.PressAsync("ArrowDown");
        var firstActive = await fund.Input.GetAttributeAsync("aria-activedescendant");
        await fund.Input.PressAsync("ArrowDown");
        var secondActive = await fund.Input.GetAttributeAsync("aria-activedescendant");
        Assert.That(secondActive, Is.Not.Null.And.Not.Empty);
        Assert.That(secondActive, Is.Not.EqualTo(firstActive), "ArrowDown must move the highlight.");

        // FR-004 — Escape closes without committing (value stays empty).
        await fund.Input.PressAsync("Escape");
        await Expect(fund.Input).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.That(await fund.CommittedValueAsync(), Is.EqualTo(string.Empty));

        // FR-004 — keyboard-only commit: type to filter, Enter on the highlighted match.
        await fund.Input.FillAsync("General");
        await Expect(fund.Input).ToHaveAttributeAsync("aria-activedescendant", new Regex(".+"));
        var expectedValue = await fund.NativeSelect.Locator("option")
            .Filter(new LocatorFilterOptions { HasText = "Fondo General" })
            .First.GetAttributeAsync("value");
        await fund.Input.PressAsync("Enter");
        Assert.That(await fund.CommittedValueAsync(), Is.EqualTo(expectedValue));
        await Expect(fund.Input).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Test]
    public async Task Combobox_BlurAfterNoMatch_RevertsToCommittedValue()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        _fundPrefix = $"Spec031US1b {suffix}";
        await SignInAsAdminAsync(suffix);
        await SearchableSeed.SeedFundsAsync(ConnectionString, _fundPrefix, 8);

        await Page.GotoAsync($"{BaseUrl}/Admin/Users");
        await Page.Locator("[data-testid=\"admin-users-cascade\"][data-ready=\"true\"]").WaitForAsync();

        var fund = new SearchableSelect(Page, "admin-users-cascade-fund");
        await Expect(fund.Input).ToBeVisibleAsync();

        // Commit a real value first.
        await fund.SelectSearchableAsync("Fondo General");
        var committed = await fund.CommittedValueAsync();
        Assert.That(committed, Is.Not.Empty);

        // FR-003 — type a no-match fragment, then blur. Typed text must NOT become
        // the value, and the input must revert to the committed option's label.
        await fund.FilterAsync("zzznomatch");
        await Expect(fund.EmptyState).ToBeVisibleAsync();
        await fund.Input.BlurAsync();

        Assert.That(await fund.CommittedValueAsync(), Is.EqualTo(committed),
            "Blur after a no-match must keep the previously committed value (FR-003).");
        await Expect(fund.Input).ToHaveValueAsync("Fondo General");
    }
}
