// Spec 031 / Polish (T026) — progressive enhancement (FR-011 / SC-005).
//
// With JavaScript disabled the enhancer never runs, so a data-driven control must
// remain a fully usable native <select>. We use /Admin/Processes/Create whose Fund
// options are server-rendered (asp-items), select a Fund via the native select, and
// confirm the Process is created — proving the page works without the combobox.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.SearchableDropdowns;

public class ProgressiveEnhancementTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    [Test]
    public async Task DataDrivenSelect_WorksWithJavaScriptDisabled()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Register + sign in on the normal (JS-on) page — auth setup is unrelated to
        // the property under test and is flaky under a JS-disabled context.
        var adminEmail = $"s031noscript_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "NoJs", "Admin", $"NJS-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        // Carry the authenticated session into a JS-DISABLED context so the enhancer
        // never runs on the page under test.
        var storageState = await Context.StorageStateAsync();
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            JavaScriptEnabled = false,
            StorageState = storageState
        });
        try
        {
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(10_000);
            page.SetDefaultNavigationTimeout(10_000);

            await page.GotoAsync($"{BaseUrl}/Admin/Processes/Create");

            // No enhancement happened: the native fund select is the only control.
            var fund = page.Locator("[data-testid=admin-process-fund-select]");
            await Expect(fund).ToBeVisibleAsync();
            await Expect(page.Locator("[data-testid=admin-process-fund-select-search]")).ToHaveCountAsync(0);

            // Native selection + submit still works end-to-end.
            await fund.SelectOptionAsync(new SelectOptionValue { Label = "Fondo General" });
            var processName = $"NoJsProc {suffix}";
            await page.Locator("[data-testid=admin-process-name-input]").FillAsync(processName);
            await page.Locator("[data-testid=admin-process-create-submit]").ClickAsync();

            await Expect(page).ToHaveURLAsync(new Regex("/Admin/Processes(\\?.*)?$"));
            var row = page.Locator("tr[data-testid^=\"admin-process-row-\"]")
                .Filter(new LocatorFilterOptions { HasText = processName });
            await Expect(row).ToContainTextAsync("Fondo General");
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
