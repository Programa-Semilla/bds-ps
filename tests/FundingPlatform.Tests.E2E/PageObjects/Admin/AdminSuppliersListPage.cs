using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

public class AdminSuppliersListPage : AdminBasePage
{
    public AdminSuppliersListPage(IPage page) : base(page) { }

    public ILocator Table => Page.GetByTestId("admin-suppliers-table");
    public ILocator Rows => Page.Locator("[data-testid^='admin-supplier-row-']");
    public ILocator StatusFilter => Page.GetByTestId("admin-suppliers-status-filter");
    public ILocator LegalIdFilter => Page.GetByTestId("admin-suppliers-legalid-filter");
    public ILocator NameFilter => Page.GetByTestId("admin-suppliers-name-filter");
    public ILocator IncompleteFilter => Page.GetByTestId("admin-suppliers-incomplete-filter");
    public ILocator FilterForm => Page.GetByTestId("admin-suppliers-filter-form");

    public ILocator RowFor(int supplierId) => Page.GetByTestId($"admin-supplier-row-{supplierId}");
    public ILocator RowDetailLink(int supplierId) => RowFor(supplierId).GetByTestId("row-action-detail");

    public Task GoToAsync(string baseUrl) => Page.GotoAsync($"{baseUrl}/Admin/Suppliers");

    public async Task FilterByStatusAsync(string statusValue)
    {
        await StatusFilter.SelectOptionAsync(statusValue);
        await FilterForm.Locator("button[type=submit]").ClickAsync();
    }

    public async Task SearchByLegalIdAsync(string text)
    {
        // Spec 026 — map the seed the same way SupplierPage does so the admin-list
        // filter matches the canonical value stored when the supplier was created.
        await LegalIdFilter.FillAsync(SupplierPage.CanonicalSupplierLegalId(text));
        await FilterForm.Locator("button[type=submit]").ClickAsync();
    }

    public async Task OpenSupplierAsync(int supplierId)
    {
        await RowDetailLink(supplierId).ClickAsync();
    }
}
