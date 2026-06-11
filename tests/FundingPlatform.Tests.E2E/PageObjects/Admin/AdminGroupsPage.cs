using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>Spec 016 / Story 1 — POM for the admin Groups CRUD screens.</summary>
public class AdminGroupsPage : AdminBasePage
{
    public AdminGroupsPage(IPage page) : base(page)
    {
    }

    // List
    public ILocator AreaWrapper => Page.Locator("[data-testid=\"admin-groups-area\"]");
    public ILocator Table => Page.Locator("[data-testid=\"admin-groups-table\"]");
    public ILocator Rows => Table.Locator("tbody tr");
    public new ILocator EmptyState => Page.Locator("[data-testid=\"admin-groups-empty\"]");
    // Spec 024 — flash messages now surface as toasts (success-banner test id preserved).
    public ILocator FlashMessage => Page.Locator("[data-testid=\"success-banner\"]");

    public ILocator RowFor(string name) =>
        Page.Locator("tr[data-testid^=\"admin-group-row-\"]")
            .Filter(new() { HasText = name });

    public ILocator RowEditButton(string name) =>
        RowFor(name).Locator("[data-testid=\"admin-group-edit\"]");

    public ILocator RowMemberCount(string name) =>
        RowFor(name).Locator("[data-testid=\"admin-group-member-count\"]");

    /// <summary>Spec 021 / FR-001 — the owning-Process column on a Groups-index row.</summary>
    public ILocator RowProcess(string name) =>
        RowFor(name).Locator("[data-testid=\"admin-group-process\"]");

    /// <summary>Spec 029 — the owning-Fund column on a Groups-index row.</summary>
    public ILocator RowFund(string name) =>
        RowFor(name).Locator("[data-testid=\"admin-group-fund\"]");

    // Edit form. Spec 021 / FR-001 — Group *creation* moved to the Process
    // detail page (see ProcessAdminPage.CreateGroupAsync); this POM keeps the
    // rename / reparent / delete surface only.
    public ILocator NameInput => Page.Locator("[data-testid=\"admin-group-name-input\"]");
    public ILocator NameError => Page.Locator("[data-testid=\"admin-group-name-error\"]");
    public ILocator ValidationSummary => Page.Locator("[data-testid=\"admin-group-validation-summary\"]");
    // Edit reparent drill-down (Fondo → Proceso). The Proceso select posts ProcessId.
    public ILocator FundSelect => Page.Locator("[data-testid=\"admin-group-reparent-fund\"]");
    public ILocator ProcessSelect => Page.Locator("[data-testid=\"admin-group-reparent-process\"]");
    public ILocator ProcessError => Page.Locator("[data-testid=\"admin-group-process-error\"]");
    public ILocator EditSubmit => Page.Locator("[data-testid=\"admin-group-edit-submit\"]");
    public ILocator DeleteSubmit => Page.Locator("[data-testid=\"admin-group-delete-submit\"]");

    public Task GoToIndexAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Groups");

    public async Task RenameGroupAsync(string newName)
    {
        await NameInput.FillAsync(newName);
        await EditSubmit.ClickAsync();
    }

    public async Task DeleteGroupAsync()
    {
        // Spec 024 — the Delete button (formaction) now opens the shared confirm modal;
        // click confirm to submit. requestSubmit(trigger) preserves the formaction.
        await DeleteSubmit.ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
    }
}
