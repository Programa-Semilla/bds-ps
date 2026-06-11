// Spec 031 — Page-object helper for the searchable-dropdown combobox produced
// by wwwroot/js/searchable-select.js. Drives the enhanced control the way a user
// does (type into the combobox input, click the matching option) while asserting
// the committed value against the still-present native <select>.
//
// Contract: contracts/searchable-select.md §5. The combobox input carries
// data-testid="<sourceTestId>-search"; the native select keeps data-testid="<sourceTestId>".
// Below-threshold / non-enhanced controls are NOT comboboxes — keep using
// ILocator.SelectOptionAsync against the native select for those.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public sealed class SearchableSelect
{
    private readonly IPage _page;
    private readonly string _sourceTestId;

    public SearchableSelect(IPage page, string sourceTestId)
    {
        _page = page;
        _sourceTestId = sourceTestId;
    }

    /// <summary>The combobox text input.</summary>
    public ILocator Input => _page.Locator($"[data-testid=\"{_sourceTestId}-search\"]");

    /// <summary>The native &lt;select&gt; that still holds the posted value.</summary>
    public ILocator NativeSelect => _page.Locator($"[data-testid=\"{_sourceTestId}\"]");

    /// <summary>The enhancer root wrapping this control (scopes option lookups).</summary>
    private ILocator Root =>
        _page.Locator($"[data-searchable-root]:has([data-testid=\"{_sourceTestId}-search\"])");

    /// <summary>The option locators currently shown in the listbox.</summary>
    public ILocator Options => Root.Locator(".fl-searchable-option");

    /// <summary>The "Sin coincidencias" empty-state locator.</summary>
    public ILocator EmptyState => Root.Locator(".fl-searchable-empty");

    /// <summary>Type a fragment into the combobox without committing.</summary>
    public async Task FilterAsync(string text)
    {
        await Input.ClickAsync();
        await Input.FillAsync(text);
    }

    /// <summary>Type a fragment and click the option whose label matches it.</summary>
    public async Task SelectSearchableAsync(string labelFragment)
    {
        await FilterAsync(labelFragment);
        var option = Options.Filter(new LocatorFilterOptions { HasText = labelFragment }).First;
        await option.ClickAsync();
    }

    /// <summary>The value currently committed on the native select.</summary>
    public Task<string> CommittedValueAsync() => NativeSelect.InputValueAsync();
}
