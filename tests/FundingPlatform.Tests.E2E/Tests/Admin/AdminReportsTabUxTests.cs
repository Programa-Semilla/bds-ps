using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / US6 / FR-021–FR-022 — verifies post-Phase-6 markup state:
///  - report-subtabs nav uses .fl-pill-tabs and chip elements use .fl-pill-tab
///  - exactly one chip carries .active per active tab
///  - KPI tile numerics expose data-ticker-target so motion.js can animate
/// </summary>
public class AdminReportsTabUxTests : AuthenticatedTestBase
{
    private async Task RegisterAndLoginAsAdminAsync(IPage page, string email, string password)
    {
        await RegisterUserAsync(page, email, password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(page, email, password);
    }

    [Test]
    public async Task ReportsDashboard_PillTabsAndKpiTicker_RenderCorrectly()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_rpt_ux_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/Reports");

        // Pill-tabs nav exists and uses the warm-modern class set.
        var nav = Page.Locator("[data-testid=report-subtabs]");
        await Expect(nav).ToBeVisibleAsync();
        var classes = await nav.GetAttributeAsync("class") ?? string.Empty;
        Assert.That(classes, Does.Contain("fl-pill-tabs"),
            "Reports nav must adopt .fl-pill-tabs (FR-021).");

        // Each chip carries .fl-pill-tab.
        var chips = Page.Locator("[data-testid=report-subtab]");
        var chipCount = await chips.CountAsync();
        Assert.That(chipCount, Is.GreaterThanOrEqualTo(4),
            "Reports nav should render at least 4 chips (Applications, Applicants, FundedItems, Aging).");

        for (int i = 0; i < chipCount; i++)
        {
            var chipClass = await chips.Nth(i).GetAttributeAsync("class") ?? string.Empty;
            Assert.That(chipClass, Does.Contain("fl-pill-tab"),
                $"Chip {i} must adopt .fl-pill-tab (FR-021).");
        }

        // At least one KPI numeric tile carries data-ticker-target — drives the
        // motion.js ticker animation per FR-022 + research §9.
        var ticker = Page.Locator("[data-ticker-target]").First;
        await Expect(ticker).ToBeVisibleAsync();
        var target = await ticker.GetAttributeAsync("data-ticker-target");
        Assert.That(target, Is.Not.Null.And.Not.Empty,
            "Numeric KPI tiles must expose data-ticker-target for motion.js.");
    }

    [Test]
    public async Task ReportsAging_ActiveChip_IsSingularAndMatchesCurrentTab()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_rpt_ag_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/Reports/Aging");

        var activeChips = Page.Locator("[data-testid=report-subtab].active");
        Assert.That(await activeChips.CountAsync(), Is.EqualTo(1),
            "Exactly one chip must be marked active at a time.");
        Assert.That(await activeChips.GetAttributeAsync("data-tab"), Is.EqualTo("Aging"),
            "The active chip on /Admin/Reports/Aging must be Aging.");
        Assert.That(await activeChips.GetAttributeAsync("aria-selected"), Is.EqualTo("true"),
            "The active chip carries aria-selected=true (FR-021 a11y).");
    }
}
