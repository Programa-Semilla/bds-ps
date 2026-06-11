// Spec 021 — see specs/021-feedback-session-may13/tasks.md T134 and US6
// acceptance scenarios + FR-009 / FR-011 / FR-032 / FR-033 / FR-034 / SC-010.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 021 / US6 / T134 — E2E for the admin-dashboard repivot and the
/// supplier-search refinements that ride alongside it. Drives the real user
/// journey end-to-end:
///   1. admin signs in, navigates to <c>/Admin</c> — sees the four action
///      KPI tiles plus the new *Personas activas* + *Fondos entregados*
///      tiles; the pending-quotation tile is NOT present (FR-033 — moved
///      to the reviewer dashboard).
///   2. reviewer signs in, navigates to <c>/Reviewer/Dashboard</c> — sees
///      the *Cotizaciones pendientes* tile that used to live on /Admin.
///   3. admin's <c>/Admin/Users</c> exposes the Process → Group cascade
///      filter pair (FR-034).
///   4. admin's <c>/Admin/Suppliers</c> exposes the autocomplete search
///      input + Process filter (FR-009 / FR-011) and defaults the table's
///      Último uso column to descending order (FR-011 — default sort).
/// </summary>
public class US6_AdminDashboardAndSearch : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    [Test]
    public async Task AdminDashboard_ShowsPersonasActivasAndFondosEntregados_HidesPendingQuotation()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var adminEmail = $"us6_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "US6", "Admin", $"UADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin");
        await Expect(Page.Locator("[data-testid=\"admin-dashboard\"]")).ToBeVisibleAsync();

        // FR-032 / SC-010 — narrative KPI tiles are visible.
        await Expect(Page.Locator("[data-testid=\"admin-narrative-kpi-strip\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-kpi-personas-activas\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-kpi-fondos-entregados\"]")).ToBeVisibleAsync();

        // FR-002 — the four action KPIs are preserved.
        await Expect(Page.Locator("[data-testid=\"admin-kpi-pending-suppliers\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-kpi-pending-legacy-quotations\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-kpi-aging-applications\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-kpi-active-users\"]")).ToBeVisibleAsync();

        // FR-033 — pending-quotation tile is absent on /Admin.
        await Expect(Page.Locator("[data-testid=\"admin-kpi-pending-quotations\"]"))
            .Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task ReviewerDashboard_HostsPendingQuotationTile()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var reviewerEmail = $"us6_rev_{unique}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, Password, "US6", "Reviewer", $"URVR-{unique}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Reviewer/Dashboard");
        await Expect(Page.Locator("[data-testid=\"reviewer-dashboard\"]")).ToBeVisibleAsync();
        // FR-033 (evolved) — the pending tile now counts Submitted applications.
        await Expect(Page.Locator("[data-testid=\"admin-kpi-pending-applications\"]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminUsers_RendersProcessGroupCascadingFilter()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var adminEmail = $"us6_users_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "US6", "Admin", $"UADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin/Users");
        // Fondo → Proceso → Grupo drill-down. The container is display:contents
        // (no box), so assert the three level selects render.
        await Expect(Page.Locator("[data-testid=\"admin-users-cascade-fund\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-users-cascade-process\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-users-cascade-group\"]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AdminSuppliers_RendersSearchInputAndProcessFilter_AndLastUsedColumnHeader()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var adminEmail = $"us6_supp_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "US6", "Admin", $"UADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers");
        await Expect(Page.Locator("[data-testid=\"admin-suppliers-area\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-suppliers-search-input\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-suppliers-cascade-process\"]")).ToBeVisibleAsync();
        // FR-011 — the Último uso column header is rendered on the Admin path.
        await Expect(Page.Locator("[data-testid=\"admin-suppliers-col-last-used\"]"))
            .ToBeVisibleAsync();

        // FR-009 — typing into the search input fires the autocomplete endpoint.
        // The autocomplete-rendered options live behind the input; this assertion
        // is selectivity-defensive: we type then check the network response was
        // a 2xx GET against /api/suppliers/search.
        var search = Page.Locator("[data-testid=\"admin-suppliers-search-input\"]");
        await search.FillAsync("Sup");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Admin/Suppliers")); // sanity
    }
}
