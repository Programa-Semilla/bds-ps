using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / US4 — sidebar admin grouping. Admin users see an "Administración"
/// section header (slug `admin-section` via data-section-testid). The header is
/// now a collapsable accordion toggle (no navigation); its sub-entries live in
/// a collapse that opens on click. The pre-existing `sidebar-entry-admin` testid
/// stays on the same element for back-compat. Non-Admin users see no admin section.
/// </summary>
public class AdminSidebarGroupingTests : AuthenticatedTestBase
{
    private async Task RegisterAndLoginAsAdminAsync(IPage page, string email, string password)
    {
        await RegisterUserAsync(page, email, password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(page, email, password);
    }

    [Test]
    public async Task Admin_SeesSectionHeaderAndAllSubEntries()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_sidebar_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin");

        var basePage = new ApplicationPage(Page);

        // Section header carries data-section-testid="admin-section" and is an
        // accordion toggle (collapsable groups) — it controls a collapse, not an href.
        var header = Page.Locator("[data-section-testid=admin-section]");
        await Expect(header).ToBeVisibleAsync();
        Assert.That(await header.GetAttributeAsync("data-bs-toggle"), Is.EqualTo("collapse"),
            "Collapsable groups — the header toggles its section instead of navigating.");

        // On /Admin the Administración section auto-expands; its children are
        // visible, including the re-added Panel landing that preserves /Admin.
        await basePage.ExpandSidebarSectionAsync("admin-section");
        foreach (var slug in new[] { "admin-home", "users", "suppliers", "reports", "currencies", "exchange-rates", "system-config" })
        {
            await Expect(Page.Locator($"[data-testid=sidebar-entry-{slug}]")).ToBeVisibleAsync();
        }

        // Accordion: opening Proceso reveals its children (incl. impact-templates,
        // groups, legacy-quotations, processes) and collapses Administración.
        await basePage.ExpandSidebarSectionAsync("proceso-section");
        foreach (var slug in new[] { "processes", "groups", "impact-templates", "legacy-quotations" })
        {
            await Expect(Page.Locator($"[data-testid=sidebar-entry-{slug}]")).ToBeVisibleAsync();
        }
        await Expect(Page.Locator("[data-testid=sidebar-entry-users]")).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Applicant_DoesNotSeeAdminSection()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"applicant_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "App", "Licant", $"LID-{Guid.NewGuid():N}"[..16]);
        await LoginAsync(Page, email, "Test123!");
        await Page.GotoAsync($"{BaseUrl}/");

        var header = Page.Locator("[data-section-testid=admin-section]");
        Assert.That(await header.CountAsync(), Is.EqualTo(0),
            "FR-017 — Applicants see no admin section.");

        // None of the admin sub-entries should render.
        foreach (var slug in new[] { "impact-templates", "users", "groups", "suppliers", "reports", "currencies", "exchange-rates", "legacy-quotations", "system-config" })
        {
            Assert.That(await Page.Locator($"[data-testid=sidebar-entry-{slug}]").CountAsync(),
                Is.EqualTo(0),
                $"FR-017 — Applicant must not see sidebar-entry-{slug}.");
        }
    }
}
