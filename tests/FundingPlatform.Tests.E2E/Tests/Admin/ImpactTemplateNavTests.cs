// Spec 021 / US10 / FR-042 / SC-019 — impact-template sidebar discoverability.
// See specs/021-feedback-session-may13/plan-impact-template-nav.md.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// US10 closes a discoverability gap: the /Admin/ImpactTemplates CRUD surface
/// already existed (and is linked from the dashboard capability card), but the
/// US1 Process-pivot rebuilt the sidebar and dropped the direct nav entry.
/// These tests drive the REAL menu journey (no deep-link to the MVC route):
///   1. Admin clicks the sidebar entry, lands on the list, creates a template.
///   2. A SupplierAdmin-only user's narrowed sidebar does NOT show the entry.
/// </summary>
public class ImpactTemplateNavTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    private async Task RegisterAndLoginAsAdminAsync(string email)
    {
        await RegisterUserAsync(Page, email, Password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await AssignRoleAsync(email, "Admin");
        await LoginAsync(Page, email, Password);
    }

    [Test]
    public async Task Admin_ReachesImpactTemplateList_ViaSidebar_AndCanCreate()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync($"admin_itnav_{unique}@example.com");

        // Land on an authenticated surface so the sidebar partial renders.
        await Page.GotoAsync($"{BaseUrl}/Admin");

        var adminPage = new AdminPage(Page);

        // impact-templates lives under the collapsable Proceso group — expand it
        // first (real menu journey, no deep-link).
        await adminPage.ExpandSidebarSectionAsync("proceso-section");

        // ----- (1) Sidebar entry exists and is the navigation path (FR-042/AC-1). -----
        var sidebarEntry = Page.Locator("[data-testid=sidebar-entry-impact-templates]");
        await Expect(sidebarEntry).ToBeVisibleAsync();
        Assert.That(await sidebarEntry.GetAttributeAsync("href"), Is.EqualTo("/Admin/ImpactTemplates"),
            "FR-042 — sidebar entry links to the impact-template list.");

        // ----- (2) Click it (real journey, no Goto) → list loads (AC-2). -----
        await sidebarEntry.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ImpactTemplates"));

        await Expect(adminPage.CreateNewTemplateButton).ToBeVisibleAsync();

        // ----- (3) Create a template to prove the surface is fully usable (SC-019). -----
        var templateName = $"Nav Template {unique}";
        await adminPage.CreateNewTemplateButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/CreateTemplate"));
        await adminPage.TemplateNameInput.FillAsync(templateName);
        await adminPage.TemplateDescriptionInput.FillAsync("Plantilla creada vía navegación del menú lateral");
        await adminPage.AddParameterButton.ClickAsync();
        await adminPage.FillParameterAsync(0, "beneficiarios", "Beneficiarios", "Integer", true, 0);
        await adminPage.SubmitButton.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/ImpactTemplates"));
        await Expect(Page.Locator($"table tbody tr:has-text('{templateName}')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SupplierAdminOnly_SidebarLacksImpactTemplateEntry()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var email = $"supplier_admin_itnav_{unique}@example.com";
        await RegisterUserAsync(Page, email, Password, "Supplier", "Admin", $"SPADM-{unique}");
        await AssignRoleAsync(email, "Auditor");
        await LoginAsync(Page, email, Password);

        // Canonical SupplierAdmin landing surface; the sidebar partial renders here.
        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers");

        // The narrowed variant is in play, and the impact-template entry is absent (AC-3).
        await Expect(Page.Locator("[data-testid=sidebar-supplier-admin-variant]")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("[data-testid=sidebar-entry-impact-templates]").CountAsync(),
            Is.EqualTo(0),
            "FR-042 — SupplierAdmin-only sidebar must not expose the impact-template entry.");
    }
}
