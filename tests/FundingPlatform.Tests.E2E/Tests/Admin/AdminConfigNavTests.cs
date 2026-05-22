// Spec 021 / US11 / FR-043 / SC-020 — system-config sidebar discoverability.
// See specs/021-feedback-session-may13/plan-admin-config-nav.md.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Sibling of <see cref="ImpactTemplateNavTests"/>: the /Admin/Configuration
/// surface (working CRUD + dashboard card) lost its sidebar entry in the US1
/// Process-pivot. These tests drive the REAL menu journey (no deep-link):
///   1. Admin clicks the sidebar entry and lands on the configuration surface.
///   2. A SupplierAdmin-only user's narrowed sidebar does NOT show the entry.
/// </summary>
public class AdminConfigNavTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    [Test]
    public async Task Admin_ReachesSystemConfiguration_ViaSidebar()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin_cfgnav_{unique}@example.com";
        await RegisterUserAsync(Page, email, Password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(Page, email, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin");

        // ----- (1) Sidebar entry exists and points at the config surface (FR-043/AC-1). -----
        var sidebarEntry = Page.Locator("[data-testid=sidebar-entry-system-config]");
        await Expect(sidebarEntry).ToBeVisibleAsync();
        Assert.That(await sidebarEntry.GetAttributeAsync("href"), Is.EqualTo("/Admin/Configuration"),
            "FR-043 — sidebar entry links to the system-configuration surface.");

        // ----- (2) Click it (real journey, no Goto) → config surface loads (AC-2/SC-020). -----
        await sidebarEntry.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Configuration"));
        await Expect(Page.Locator($"h2:has-text('{UiCopy.SystemConfiguration}')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SupplierAdminOnly_SidebarLacksSystemConfigEntry()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var email = $"supplier_admin_cfgnav_{unique}@example.com";
        await RegisterUserAsync(Page, email, Password, "Supplier", "Admin", $"SPADM-{unique}");
        await AssignRoleAsync(email, "SupplierAdmin");
        await LoginAsync(Page, email, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers");

        await Expect(Page.Locator("[data-testid=sidebar-supplier-admin-variant]")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("[data-testid=sidebar-entry-system-config]").CountAsync(),
            Is.EqualTo(0),
            "FR-043 — SupplierAdmin-only sidebar must not expose the system-config entry.");
    }
}
