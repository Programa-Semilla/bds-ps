using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

public class AdminUsersListPage : AdminBasePage
{
    public AdminUsersListPage(IPage page) : base(page)
    {
    }

    public ILocator Table => Page.Locator("[data-testid=\"admin-users-table\"]");
    public ILocator Rows => Table.Locator("tbody tr[data-testid^=\"admin-user-row-\"]");
    public ILocator CreateButton => Page.Locator("[data-testid=\"admin-users-create-button\"]");
    public ILocator SearchBox => Page.Locator("[data-testid=\"admin-users-search\"]");
    public ILocator RoleFilter => Page.Locator("[data-testid=\"admin-users-role-filter\"]");
    public ILocator StatusFilter => Page.Locator("[data-testid=\"admin-users-status-filter\"]");
    public ILocator FilterSubmit => Page.Locator("[data-testid=\"admin-users-filter-submit\"]");
    public ILocator EmptyStateRegion => Page.Locator("[data-testid=\"admin-users-empty\"]");
    public ILocator PaginationContainer => Page.Locator("[data-testid=\"admin-users-pagination\"]");

    public ILocator RowFor(string email) =>
        Page.Locator($"[data-testid=\"admin-user-row-{email}\"]");

    public ILocator RowEditLink(string email) =>
        RowFor(email).Locator("[data-testid=\"row-action-edit\"]");

    public ILocator RowDisableButton(string email) =>
        RowFor(email).Locator("[data-testid=\"row-action-disable\"]");

    public ILocator RowEnableButton(string email) =>
        RowFor(email).Locator("[data-testid=\"row-action-enable\"]");

    public ILocator RowResetPasswordLink(string email) =>
        RowFor(email).Locator("[data-testid=\"row-action-reset-password\"]");

    // Spec 033 / US2 — "Reenviar invitación" row action.
    public ILocator RowResendInviteButton(string email) =>
        RowFor(email).Locator("[data-testid=\"row-action-resend-invite\"]");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Users");

    public async Task ClickCreateAsync()
    {
        await CreateButton.ClickAsync();
    }

    public async Task SearchAsync(string text)
    {
        await SearchBox.FillAsync(text);
        await FilterSubmit.ClickAsync();
    }

    public async Task FilterByRoleAsync(string role)
    {
        await RoleFilter.SelectOptionAsync(role);
        await FilterSubmit.ClickAsync();
    }

    public async Task FilterByStatusAsync(string status)
    {
        await StatusFilter.SelectOptionAsync(status);
        await FilterSubmit.ClickAsync();
    }
}
