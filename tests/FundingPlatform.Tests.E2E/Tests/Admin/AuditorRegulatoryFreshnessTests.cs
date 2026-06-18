using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 038 (US2) — regulatory changes are auditable, each status shows
/// last-reviewed recency, and the auditor can re-confirm a value without changing
/// it. The "Confirmar revisión" control is absent until a status has a value (D9).
/// </summary>
public class AuditorRegulatoryFreshnessTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    [Test]
    public async Task ChangingStatus_ShowsFreshness_ConfirmReview_Refreshes_UnsetHasNoControl()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var supplierId = await SupplierSeed.SeedVerifiedSupplierAsync(
            ConnectionString, $"3-102-{suffix}", $"Proveedor {suffix}");

        var email = $"auditor_fresh_{suffix}@example.com";
        await RegisterUserAsync(Page, email, Password, "Aud", "Fresh", $"AUF-{suffix}");
        await AssignRoleAsync(email, "Auditor");
        await LoginAsync(Page, email, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");

        // Set only Hacienda (al día = 2); leave CCSS/SICOP unset.
        await Page.Locator("[data-testid=\"admin-supplier-hacienda-select\"]").SelectOptionAsync("2");
        await Page.Locator("[data-testid=\"admin-supplier-edit-submit\"]").ClickAsync();

        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");

        // Freshness shows "revisado hoy" for the set field.
        await Expect(Page.Locator("[data-testid=\"hacienda-freshness\"]")).ToContainTextAsync("revisado hoy");

        // Confirm-review control exists for the set field, absent for the unset CCSS.
        await Expect(Page.Locator("[data-testid=\"confirm-review-hacienda\"]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"confirm-review-ccss\"]")).ToHaveCountAsync(0);

        // Re-confirm Hacienda without changing the value.
        await Page.Locator("[data-testid=\"confirm-review-hacienda\"]").ClickAsync();
        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");
        Assert.That(await Page.Locator("[data-testid=\"admin-supplier-hacienda-select\"]").InputValueAsync(), Is.EqualTo("2"));
        await Expect(Page.Locator("[data-testid=\"hacienda-freshness\"]")).ToContainTextAsync("revisado hoy");
    }

    [Test]
    public async Task AdminActivityFeed_ShowsSupplierRegulatoryEvent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var supplierId = await SupplierSeed.SeedVerifiedSupplierAsync(
            ConnectionString, $"3-103-{suffix}", $"Proveedor {suffix}");

        var email = $"auditor_feed_{suffix}@example.com";
        await RegisterUserAsync(Page, email, Password, "Aud", "Feed", $"AUFE-{suffix}");
        await AssignRoleAsync(email, "Auditor");
        await LoginAsync(Page, email, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");
        await Page.Locator("[data-testid=\"admin-supplier-hacienda-select\"]").SelectOptionAsync("2");
        await Page.Locator("[data-testid=\"admin-supplier-edit-submit\"]").ClickAsync();

        // The seeded platform admin can see the activity feed on /Admin.
        await LoginAsync(Page, "demo-admin@programa-semilla.test", "Demo123!");
        await Page.GotoAsync($"{BaseUrl}/Admin");
        await Expect(Page.Locator("body")).ToContainTextAsync("cumplimiento regulatorio del proveedor");
    }
}
