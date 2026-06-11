using System.Text.Json;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 016 / Story 2 + spec 029 — POM helpers for the group control on the
/// admin user create/edit forms. The control is the Fondo → Proceso → Grupo
/// drill-down (spec 029): Fund + Process are cascading filters and the Group
/// level is a checkbox list whose checked items accumulate as removable chips,
/// preserved across filter changes. The posted contract is unchanged (GroupIds[]).
/// Composes with <see cref="AdminUserCreatePage"/> and <see cref="AdminUserEditPage"/>.
/// </summary>
public class AdminUserFormPage : AdminBasePage
{
    public AdminUserFormPage(IPage page) : base(page) { }

    public ILocator GroupsField => Page.Locator("[data-testid=\"admin-user-groups-field\"]");
    public ILocator GroupSelector => Page.Locator("[data-testid=\"group-drilldown-selector\"]");
    public ILocator FundSelect => Page.Locator("[data-testid=\"group-selector-fund\"]");
    public ILocator ProcessSelect => Page.Locator("[data-testid=\"group-selector-process\"]");
    public ILocator ChipsBox => Page.Locator("[data-testid=\"group-selector-chips\"]");
    public ILocator GroupsError => Page.Locator("[data-testid=\"admin-user-groups-error\"]");
    public ILocator GroupsEmptyState => Page.Locator("[data-testid=\"admin-user-groups-empty\"]");
    public ILocator ConcurrencyStamp => Page.Locator("[data-testid=\"admin-user-concurrency-stamp\"]");
    public ILocator RoleSelect => Page.Locator("[data-testid=\"admin-user-role\"]");

    private ILocator GroupCheckbox(string groupId) =>
        Page.Locator($"[data-testid=\"group-option-{groupId}\"]");

    /// <summary>Waits until the drill-down JS has initialized (chips + hidden
    /// inputs materialized from data-selected), so reads don't race the static
    /// initial markup.</summary>
    public Task WaitForReadyAsync() =>
        Page.Locator("[data-testid=\"group-drilldown-selector\"][data-ready=\"true\"]")
            .WaitForAsync();

    /// <summary>
    /// Sets the selection to exactly the given groups (by visible name) by
    /// driving the drill-down: clears any current selection, then for each group
    /// picks the owning Fund + Process and ticks the group checkbox. Replace
    /// semantics (mirrors the old multi-select's <c>SelectOptionAsync</c>) so a
    /// caller that pre-selected all groups can narrow to an exact set. The result
    /// can still span several processes/funds (multi-group membership).
    /// </summary>
    public async Task SelectGroupsAsync(params string[] groupNames)
    {
        await WaitForReadyAsync();
        await ClearGroupSelectionAsync();
        foreach (var name in groupNames)
        {
            var resolved = await ResolveGroupAsync(name)
                ?? throw new InvalidOperationException(
                    $"Group '{name}' was not found in the drill-down catalog (data-catalog).");
            await FundSelect.SelectOptionAsync(new SelectOptionValue { Value = resolved.FundId });
            await ProcessSelect.SelectOptionAsync(new SelectOptionValue { Value = resolved.ProcessId });
            await GroupCheckbox(resolved.GroupId).CheckAsync();
        }
    }

    /// <summary>
    /// Selects every group present in the drill-down catalog. Used by
    /// <see cref="AdminUserCreatePage.FillAsync"/> to keep pre-016 callers (which
    /// only pass the basic fields) green by satisfying the ≥1-group rule.
    /// </summary>
    public async Task SelectAllGroupsAsync()
    {
        var names = new List<string>();
        var json = await GroupSelector.GetAttributeAsync("data-catalog");
        if (string.IsNullOrEmpty(json)) return;
        using var doc = JsonDocument.Parse(json);
        foreach (var fund in doc.RootElement.EnumerateArray())
        {
            foreach (var proc in fund.GetProperty("processes").EnumerateArray())
            {
                foreach (var g in proc.GetProperty("groups").EnumerateArray())
                {
                    var n = g.GetProperty("name").GetString();
                    if (n is not null) names.Add(n);
                }
            }
        }
        if (names.Count > 0)
        {
            await SelectGroupsAsync(names.ToArray());
        }
    }

    /// <summary>Clears all selected groups by removing every chip.</summary>
    public async Task ClearGroupSelectionAsync()
    {
        var removeButtons = ChipsBox.Locator("[data-remove]");
        // Each removal re-renders the chip list, so re-query and pop the first
        // until none remain.
        while (await removeButtons.CountAsync() > 0)
        {
            await removeButtons.First.ClickAsync();
        }
    }

    public Task<bool> IsGroupsFieldVisibleAsync() =>
        GroupsField.IsVisibleAsync();

    /// <summary>Returns the names of the currently-selected groups (the chips).</summary>
    public async Task<IReadOnlyList<string>> GetSelectedGroupNamesAsync()
    {
        await WaitForReadyAsync();
        var chips = ChipsBox.Locator("[data-group-name]");
        var count = await chips.CountAsync();
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var n = await chips.Nth(i).GetAttributeAsync("data-group-name");
            if (n is not null) names.Add(n);
        }
        return names;
    }

    private async Task<ResolvedGroup?> ResolveGroupAsync(string groupName)
    {
        var json = await GroupSelector.GetAttributeAsync("data-catalog");
        if (string.IsNullOrEmpty(json)) return null;
        using var doc = JsonDocument.Parse(json);
        foreach (var fund in doc.RootElement.EnumerateArray())
        {
            var fundId = fund.GetProperty("id").GetInt32().ToString();
            foreach (var proc in fund.GetProperty("processes").EnumerateArray())
            {
                var processId = proc.GetProperty("id").GetInt32().ToString();
                foreach (var g in proc.GetProperty("groups").EnumerateArray())
                {
                    if (string.Equals(g.GetProperty("name").GetString(), groupName, StringComparison.Ordinal))
                    {
                        return new ResolvedGroup(fundId, processId, g.GetProperty("id").GetInt32().ToString());
                    }
                }
            }
        }
        return null;
    }

    private sealed record ResolvedGroup(string FundId, string ProcessId, string GroupId);
}
