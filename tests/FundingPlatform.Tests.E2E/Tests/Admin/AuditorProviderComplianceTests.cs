using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 038 (US1) — an Auditor sets the enumerated Hacienda/CCSS/SICOP statuses +
/// PME/PYME on a provider via dropdowns; the electronic-invoice control is gone;
/// values persist; the Auditor is scoped to /Admin/Suppliers*.
/// </summary>
public class AuditorProviderComplianceTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    private async Task<string> ProvisionAuditorAsync(string suffix)
    {
        var email = $"auditor_{suffix}@example.com";
        await RegisterUserAsync(Page, email, Password, "Aud", "Itor", $"AUD-{suffix}");
        await AssignRoleAsync(email, "Auditor");
        await LoginAsync(Page, email, Password);
        return email;
    }

    [Test]
    public async Task Auditor_SetsStatusesAndPme_NoEInvoiceControl_Persists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var supplierId = await SupplierSeed.SeedVerifiedSupplierAsync(
            ConnectionString, $"3-101-{suffix}", $"Proveedor {suffix}");
        await ProvisionAuditorAsync(suffix);

        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");

        // The electronic-invoice control is gone (FR / US1).
        await Expect(Page.Locator("[data-testid=\"admin-supplier-einvoice-toggle\"]")).ToHaveCountAsync(0);

        // Set the three enumerated statuses (al día / al día / sin sanciones = code 2) + PME.
        await Page.Locator("[data-testid=\"admin-supplier-hacienda-select\"]").SelectOptionAsync("2");
        await Page.Locator("[data-testid=\"admin-supplier-ccss-select\"]").SelectOptionAsync("2");
        await Page.Locator("[data-testid=\"admin-supplier-sicop-select\"]").SelectOptionAsync("2");
        await Page.Locator("[data-testid=\"admin-supplier-pme-toggle\"]").CheckAsync();
        await Page.Locator("[data-testid=\"admin-supplier-edit-submit\"]").ClickAsync();

        // Reload and confirm persistence.
        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");
        Assert.That(await Page.Locator("[data-testid=\"admin-supplier-hacienda-select\"]").InputValueAsync(), Is.EqualTo("2"));
        Assert.That(await Page.Locator("[data-testid=\"admin-supplier-ccss-select\"]").InputValueAsync(), Is.EqualTo("2"));
        Assert.That(await Page.Locator("[data-testid=\"admin-supplier-sicop-select\"]").InputValueAsync(), Is.EqualTo("2"));
        await Expect(Page.Locator("[data-testid=\"admin-supplier-pme-toggle\"]")).ToBeCheckedAsync();
    }

    [Test]
    public async Task Auditor_IsScopedToSuppliers_OtherAdminRoutesReturn403()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        await ProvisionAuditorAsync(suffix);

        var response = await Page.GotoAsync($"{BaseUrl}/Admin/Users");
        Assert.That(response!.Status, Is.EqualTo(403));
        await Expect(Page.Locator("[data-testid=\"error-403-page\"]")).ToBeVisibleAsync();
    }
}
