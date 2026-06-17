using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 037 / US2 — POM for the "Empresas" management card on the admin user Edit
/// page (/Admin/Users/{id}/Edit). Drives add / rename / archive / unarchive.
/// </summary>
public sealed class AdminUserCompaniesPage : AdminBasePage
{
    public AdminUserCompaniesPage(IPage page) : base(page) { }

    public ILocator Card => Page.Locator("[data-testid=\"admin-user-companies-card\"]");
    public ILocator Rows => Page.Locator("[data-testid=\"admin-user-company-row\"]");
    public ILocator AddNameInput => Page.Locator("[data-testid=\"admin-user-company-add-name\"]");
    public ILocator AddSubmit => Page.Locator("[data-testid=\"admin-user-company-add-submit\"]");

    public ILocator RowFor(string name) =>
        Rows.Filter(new() { Has = Page.Locator($"input[value=\"{name}\"]") });

    public async Task AddCompanyAsync(string name)
    {
        await AddNameInput.FillAsync(name);
        await AddSubmit.ClickAsync();
    }

    public async Task RenameCompanyAsync(string currentName, string newName)
    {
        var row = RowFor(currentName);
        await row.Locator("[data-testid=\"admin-user-company-name-input\"]").FillAsync(newName);
        await row.Locator("[data-testid=\"admin-user-company-rename\"]").ClickAsync();
    }

    // Archive/unarchive go through the spec-024 shared confirm modal.
    private ILocator ConfirmButton => Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]");

    public async Task ArchiveCompanyAsync(string name)
    {
        await RowFor(name).Locator("[data-testid=\"admin-user-company-archive\"]").ClickAsync();
        await ConfirmButton.ClickAsync();
    }

    public async Task UnarchiveCompanyAsync(string name)
    {
        await RowFor(name).Locator("[data-testid=\"admin-user-company-unarchive\"]").ClickAsync();
        await ConfirmButton.ClickAsync();
    }
}
