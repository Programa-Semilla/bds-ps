using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 016 / Story 2 — POM helpers for the multi-select group control on the
/// admin user create/edit forms. Composes with <see cref="AdminUserCreatePage"/>
/// and <see cref="AdminUserEditPage"/>; this class only adds the shared
/// group-selector helpers.
/// </summary>
public class AdminUserFormPage : AdminBasePage
{
    public AdminUserFormPage(IPage page) : base(page) { }

    public ILocator GroupsField => Page.Locator("[data-testid=\"admin-user-groups-field\"]");
    public ILocator GroupsSelect => Page.Locator("[data-testid=\"admin-user-groups-select\"]");
    public ILocator GroupsError => Page.Locator("[data-testid=\"admin-user-groups-error\"]");
    public ILocator GroupsEmptyState => Page.Locator("[data-testid=\"admin-user-groups-empty\"]");
    public ILocator ConcurrencyStamp => Page.Locator("[data-testid=\"admin-user-concurrency-stamp\"]");
    public ILocator RoleSelect => Page.Locator("[data-testid=\"admin-user-role\"]");

    /// <summary>Selects the given groups (by visible name) in the multi-select.</summary>
    public async Task SelectGroupsAsync(params string[] groupNames)
    {
        var values = new List<SelectOptionValue>();
        foreach (var name in groupNames)
        {
            values.Add(new SelectOptionValue { Label = name });
        }
        await GroupsSelect.SelectOptionAsync(values.ToArray());
    }

    /// <summary>Clears all selected options in the multi-select.</summary>
    public async Task ClearGroupSelectionAsync()
    {
        await GroupsSelect.SelectOptionAsync(new[] { new SelectOptionValue { Index = -1 } });
    }

    public Task<bool> IsGroupsFieldVisibleAsync() =>
        GroupsField.IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetSelectedGroupNamesAsync()
    {
        var selected = await GroupsSelect.EvaluateAsync<string[]>(
            "el => Array.from(el.selectedOptions).map(o => o.text)");
        return selected ?? Array.Empty<string>();
    }
}
