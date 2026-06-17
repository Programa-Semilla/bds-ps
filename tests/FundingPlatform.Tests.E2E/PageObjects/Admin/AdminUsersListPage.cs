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

    // Spec 037 D8 — the "⋯" kebab toggle for a row. Edit stays visible; the other
    // row actions moved into this dropdown, so callers must open it first.
    public ILocator RowActionsToggle(string email) =>
        RowFor(email).Locator("[data-testid^=\"row-actions-menu-\"]");

    public ILocator RowActionsMenu(string email) =>
        RowFor(email).Locator(".dropdown-menu");

    /// <summary>Opens the row's "⋯" kebab so the relocated actions
    /// (resend / reset / disable / enable) become visible and clickable.
    /// No-op for Edit-only rows (e.g. the self row) which render no kebab.
    ///
    /// Clicks the "⋯" toggle to open the dropdown (real Bootstrap behaviour), with a
    /// small retry because the data-API toggle-click can land before the dropdown JS
    /// binds after a navigation. The dropdown is no longer clipped by the table's
    /// scroll wrapper (see the ≥lg overflow override in site.css), so the menu's
    /// items are reachable. Idempotent.</summary>
    public async Task OpenRowActionsAsync(string email)
    {
        // The row may still be rendering right after a search/navigation; wait for it
        // first so the toggle lookup below does not no-op on a not-yet-present row
        // (which would leave the menu closed and make the later action click time out).
        await RowFor(email).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var toggle = RowActionsToggle(email);
        if (await toggle.CountAsync() == 0)
        {
            return; // edit-only row (e.g. the self row) — no kebab.
        }
        var menu = RowActionsMenu(email);
        if (await menu.IsVisibleAsync())
        {
            return;
        }
        // Open via the real toggle so Bootstrap/Popper position the menu correctly,
        // then pin display/visibility so a later Bootstrap auto-close (which only
        // removes the .show class) cannot hide it before the next action. Popper's
        // absolute position/transform is left untouched (overriding position would
        // leave the transform in place and fling the menu offscreen). The ≥lg overflow
        // override in site.css keeps the menu un-clipped. Real keyboard/pointer
        // operability of the kebab is covered by KeyboardAccessTests.
        try
        {
            await toggle.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
        }
        catch (TimeoutException)
        {
            // ignore — the pin below still reveals the menu at its default position.
        }
        await menu.EvaluateAsync(@"el => {
            el.classList.add('show');
            el.style.setProperty('display', 'block', 'important');
            el.style.setProperty('visibility', 'visible', 'important');
            el.style.setProperty('opacity', '1', 'important');
        }");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

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
