// Spec 021 / T088 — POM for the inline supplier-branch registration form
// (applicant no-match path). Used by US2 E2E when the autocomplete returns
// no results and the applicant registers a new branch on the spot.
//
// The HTML for the inline form is rendered by the supplier-add surface (the
// `/Supplier/Add?appId&itemId` route from the legacy ItemController path).
// The POM exposes the key fields so US2 E2E doesn't drift on selectors.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public sealed class SupplierBranchInlineForm
{
    private readonly IPage _page;

    public SupplierBranchInlineForm(IPage page) { _page = page; }

    public ILocator AutocompleteInput => _page.Locator("input[data-supplier-autocomplete]").First;
    public ILocator ProvinceSelect => _page.Locator("[data-cascade-source=province]").First;
    public ILocator CantonSelect => _page.Locator("select[name=CantonId]").First;
    public ILocator ContactPersonNameInput => _page.Locator("input[name=ContactPersonName]").First;

    public async Task PickProvinceAsync(string provinceName)
    {
        await ProvinceSelect.SelectOptionAsync(new SelectOptionValue { Label = provinceName });
    }

    public async Task PickCantonAsync(string cantonName)
    {
        await CantonSelect.SelectOptionAsync(new SelectOptionValue { Label = cantonName });
    }
}
