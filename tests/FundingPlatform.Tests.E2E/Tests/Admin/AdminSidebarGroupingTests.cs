using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / US4 — sidebar admin grouping. Admin users see an "Administración"
/// section header (slug `admin-section` via data-section-testid) followed by
/// the admin sub-entries indented under it. The pre-existing
/// `sidebar-entry-admin` testid stays on the same element for back-compat.
/// Non-Admin users see no admin section.
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

        // Section header carries a data-section-testid="admin-section" and
        // links to /Admin per R3.
        var header = Page.Locator("[data-section-testid=admin-section]");
        await Expect(header).ToBeVisibleAsync();
        var href = await header.GetAttributeAsync("href");
        Assert.That(href, Is.EqualTo("/Admin"),
            "FR-015 + R3 — section header navigates to /Admin.");

        // All admin sub-entry slugs must be present (FR-016; +impact-templates per US10/FR-042; +system-config per US11/FR-043).
        var slugs = new[] { "impact-templates", "users", "groups", "suppliers", "reports", "currencies", "exchange-rates", "legacy-quotations", "system-config" };
        foreach (var slug in slugs)
        {
            await Expect(Page.Locator($"[data-testid=sidebar-entry-{slug}]")).ToBeVisibleAsync();
        }
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
