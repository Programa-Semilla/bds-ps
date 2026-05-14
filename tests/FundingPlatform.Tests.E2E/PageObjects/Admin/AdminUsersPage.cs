// Spec 021 — see specs/021-feedback-session-may13/tasks.md T076.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 021 / US1 / FR-034 / T076 — POM focused on the new Process → Group
/// cascading filter widget on <c>/Admin/Users</c>. The legacy
/// <see cref="AdminUsersListPage"/> still covers the role / status / search
/// filters; this page-object exposes only the additions a spec-021 test cares
/// about so the two POMs do not fight over the same surface.
/// </summary>
public class AdminUsersPage : AdminBasePage
{
    public AdminUsersPage(IPage page) : base(page)
    {
    }

    // FR-034 — cascading filter widget.
    public ILocator CascadeContainer =>
        Page.Locator("[data-testid=\"admin-users-process-group-cascade\"]");
    public ILocator ProcessFilter => Page.Locator("[data-testid=\"admin-users-process-filter\"]");
    public ILocator GroupFilter => Page.Locator("[data-testid=\"admin-users-group-filter\"]");
    public ILocator FilterSubmit => Page.Locator("[data-testid=\"admin-users-filter-submit\"]");

    // Result table reused from the legacy AdminUsersListPage testids.
    public ILocator Table => Page.Locator("[data-testid=\"admin-users-table\"]");
    public ILocator RowFor(string email) =>
        Page.Locator($"[data-testid=\"admin-user-row-{email}\"]");

    public Task GoToIndexAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Users");

    /// <summary>
    /// Returns the visible option labels in the Group dropdown — used to
    /// assert that picking a Process narrowed the dropdown to that Process's
    /// groups (FR-034 / AC #3).
    /// </summary>
    public Task<IReadOnlyList<string>> VisibleGroupOptionsAsync() =>
        GroupFilter.Locator("option").AllTextContentsAsync();

    /// <summary>
    /// Selects the Process by its visible label. The cascade JS rebuilds the
    /// Group dropdown on the resulting <c>change</c> event.
    /// </summary>
    public Task SelectProcessByLabelAsync(string label) =>
        ProcessFilter.SelectOptionAsync(new SelectOptionValue { Label = label });
}
