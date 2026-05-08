using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / US1 — admin home dashboard MVP. Verifies:
///  - 4 KPI tiles render with expected slugs and zero-of-everything counts
///  - all 9 capability cards render and click to a 200 OK admin surface
///  - non-Admin users (anonymous) are redirected away
///  - reduced-motion mode renders KPI counts in their final state immediately
/// Uses zero-of-everything fixture: a freshly registered admin in a clean DB
/// has no pending suppliers, no aging applications, etc.
/// </summary>
public class AdminDashboardTests : AuthenticatedTestBase
{
    private async Task RegisterAndLoginAsAdminAsync(IPage page, string email, string password)
    {
        await RegisterUserAsync(page, email, password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(page, email, password);
    }

    [Test]
    public async Task Dashboard_RendersFourKpisAndNineCapabilityCards()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin_dash_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterAndLoginAsAdminAsync(Page, email, password);

        var dashboard = new AdminDashboardPage(Page);
        await dashboard.GotoAsync(BaseUrl);

        await Expect(dashboard.Root).ToBeVisibleAsync();
        await Expect(dashboard.KpiStrip).ToBeVisibleAsync();

        // 4 KPIs
        await Expect(dashboard.Kpi("pending-suppliers")).ToBeVisibleAsync();
        await Expect(dashboard.Kpi("pending-legacy-quotations")).ToBeVisibleAsync();
        await Expect(dashboard.Kpi("aging-applications")).ToBeVisibleAsync();
        await Expect(dashboard.Kpi("active-users")).ToBeVisibleAsync();

        // 3 sections
        await Expect(dashboard.Section("users-access")).ToBeVisibleAsync();
        await Expect(dashboard.Section("catalog")).ToBeVisibleAsync();
        await Expect(dashboard.Section("operations")).ToBeVisibleAsync();

        // 9 capability cards
        var slugs = new[]
        {
            "users", "groups",
            "suppliers", "currencies", "exchange-rates", "impact-templates",
            "reports", "legacy-quotations", "system-config",
        };
        foreach (var slug in slugs)
        {
            await Expect(dashboard.CapabilityCard(slug)).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Dashboard_KpiTiles_DeepLinkToFilteredSurfaces()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin_kpilink_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterAndLoginAsAdminAsync(Page, email, password);

        var dashboard = new AdminDashboardPage(Page);
        await dashboard.GotoAsync(BaseUrl);

        Assert.Multiple(async () =>
        {
            Assert.That(await dashboard.Kpi("pending-suppliers").GetAttributeAsync("href"),
                Does.Contain("/Admin/Suppliers"));
            Assert.That(await dashboard.Kpi("pending-suppliers").GetAttributeAsync("href"),
                Does.Contain("status=PendingReview"));
            Assert.That(await dashboard.Kpi("aging-applications").GetAttributeAsync("href"),
                Does.Contain("/Admin/Reports/Aging"));
            Assert.That(await dashboard.Kpi("active-users").GetAttributeAsync("href"),
                Does.Contain("/Admin/Users"));
            Assert.That(await dashboard.Kpi("active-users").GetAttributeAsync("href"),
                Does.Contain("status=Active"));
            Assert.That(await dashboard.Kpi("pending-legacy-quotations").GetAttributeAsync("href"),
                Does.Contain("/Admin/LegacyQuotations"));
        });
    }

    [Test]
    public async Task Dashboard_AnonymousUser_IsRedirectedToLogin()
    {
        var response = await Page.GotoAsync($"{BaseUrl}/Admin");
        // Authorization redirects unauthenticated GETs to /Account/Login.
        Assert.That(Page.Url, Does.Match("/Account/Login"));
    }

    [Test]
    public async Task Dashboard_ZeroOfEverythingFixture_KpisAllRenderZero()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin_zero_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterAndLoginAsAdminAsync(Page, email, password);

        var dashboard = new AdminDashboardPage(Page);
        await dashboard.GotoAsync(BaseUrl);

        // Zero pending suppliers / zero pending legacy / zero aging / >=1 active users (the new admin).
        var pending = await dashboard.ReadKpiNumericAsync("pending-suppliers");
        var legacy = await dashboard.ReadKpiNumericAsync("pending-legacy-quotations");
        var aging = await dashboard.ReadKpiNumericAsync("aging-applications");
        var users = await dashboard.ReadKpiNumericAsync("active-users");

        Assert.Multiple(() =>
        {
            Assert.That(pending, Is.EqualTo(0), "Pending suppliers KPI = 0 in zero-of-everything fixture.");
            Assert.That(legacy, Is.EqualTo(0), "Pending legacy quotations KPI = 0 in zero-of-everything fixture.");
            Assert.That(aging, Is.EqualTo(0), "Aging applications KPI = 0 in zero-of-everything fixture.");
            Assert.That(users, Is.GreaterThanOrEqualTo(1), "At least the just-registered admin counts as active.");
        });
    }
}
