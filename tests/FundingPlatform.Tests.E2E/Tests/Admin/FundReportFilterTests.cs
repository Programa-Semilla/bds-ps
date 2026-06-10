using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 029 / US5 (T056) — the admin reports expose an exact Fund filter + a Fund
/// column. Validates the filter control + column render on the Applications,
/// Funded Items, and Aging reports and that applying a Fund filter keeps the
/// report on its page (no error). Exact row-level filtering is covered by the
/// ReportQueryService integration path; this asserts the UI surface end-to-end.
/// </summary>
public class FundReportFilterTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"reportadmin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Report", "Admin", $"RADM-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [Test]
    public async Task Reports_ExposeFundFilterAndColumn()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);

        foreach (var path in new[] { "/Admin/Reports/Applications", "/Admin/Reports/FundedItems", "/Admin/Reports/Aging" })
        {
            await Page.GotoAsync($"{BaseUrl}{path}");

            // The shared Fund filter dropdown is present (lists all Funds incl. the
            // seed "Fondo General"). The Fund *column* is asserted by the CSV-header
            // E2E + the report DTO integration tests; it only renders with table rows.
            var fundFilter = Page.Locator("[data-testid=report-filter-fund]");
            await Expect(fundFilter).ToBeVisibleAsync();

            // Applying the Fund filter (first real option) keeps the report on its page.
            await fundFilter.SelectOptionAsync(new SelectOptionValue { Index = 1 });
            await Page.Locator("button[type=submit]:has-text('Aplicar')").First.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(path)));
            await Expect(Page.Locator("[data-testid=report-filter-fund]")).ToBeVisibleAsync();
        }
    }
}
