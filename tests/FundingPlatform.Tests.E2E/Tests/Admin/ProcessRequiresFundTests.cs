using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 029 / US2 (T042) — every Process belongs to a Fund. Create is blocked
/// without a Fund; the selector lists only Active Funds; the Process list shows
/// a Fund column and filters by Fund.
/// </summary>
public class ProcessRequiresFundTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"procfund_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "ProcFund", "Admin", $"PFADM-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    private async Task<string> CreateFundAsync(string u)
    {
        var fundName = $"FondoP-{u}";
        await Page.GotoAsync($"{BaseUrl}/Admin/Funds/Create");
        await Page.Locator("[data-testid=admin-fund-name-input]").FillAsync(fundName);
        await Page.Locator("[data-testid=admin-fund-description-input]").FillAsync("Fondo para procesos.");
        await Page.Locator("[data-testid=admin-fund-create-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Funds(\\?.*)?$"));
        return fundName;
    }

    [Test]
    public async Task Process_Create_BlockedWithoutFund_ThenSucceedsWithActiveFund()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);
        var fundName = await CreateFundAsync(u);
        var procName = $"Proc-{u}";

        await Page.GotoAsync($"{BaseUrl}/Admin/Processes/Create");
        // The Fund selector is present and lists the Active Fund.
        var fundSelect = Page.Locator("[data-testid=admin-process-fund-select]");
        await Expect(fundSelect).ToBeVisibleAsync();

        // Submit without a Fund → blocked (required-fund validation error).
        await Page.Locator("[data-testid=admin-process-name-input]").FillAsync(procName);
        await Page.Locator("[data-testid=admin-process-create-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Processes/Create$"));
        await Expect(Page.Locator("[data-testid=admin-process-fund-error]"))
            .ToContainTextAsync(new Regex("fondo activo"));

        // Choose the Active Fund → the Process is created and shows the Fund column.
        await Page.Locator("[data-testid=admin-process-name-input]").FillAsync(procName);
        await fundSelect.SelectOptionAsync(new SelectOptionValue { Label = fundName });
        await Page.Locator("[data-testid=admin-process-create-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Processes(\\?.*)?$"));

        var row = Page.Locator("tr[data-testid^=admin-process-row-]").Filter(new() { HasText = procName });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.Locator("[data-testid=admin-process-fund]")).ToContainTextAsync(fundName);

        // Filter the Process list by the Fund → the row is still shown.
        await Page.Locator("[data-testid=admin-processes-fund-filter]").SelectOptionAsync(new SelectOptionValue { Label = fundName });
        await Page.Locator("[data-testid=admin-processes-filter-submit]").ClickAsync();
        await Expect(Page.Locator("tr[data-testid^=admin-process-row-]").Filter(new() { HasText = procName })).ToBeVisibleAsync();
    }
}
