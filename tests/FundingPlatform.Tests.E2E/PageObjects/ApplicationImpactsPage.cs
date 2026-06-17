using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 035 (evolved 2026-06-16) / US2 — POM for the application-level impacts manager
/// (<c>/Application/{appId}/Impacts</c>): declare one or more impacts (active template
/// picker → dynamic parameter fields), list them, and remove.
/// </summary>
public class ApplicationImpactsPage : BasePage
{
    public ApplicationImpactsPage(IPage page) : base(page)
    {
    }

    public ILocator TemplateSelect => Page.Locator("#impactTemplateId");
    public ILocator ParamsContainer => Page.Locator("#impact-params");
    public ILocator AddSubmit => Page.Locator("[data-testid=add-impact-submit]");
    public ILocator DeclaredImpactRows => Page.Locator("[data-testid=declared-impact-row]");
    public ILocator DeclaredImpactNames => Page.Locator("[data-testid=declared-impact-name]");
    public ILocator EmptyState => Page.Locator("[data-testid=impacts-empty]");
    public ILocator NoActiveTemplatesAlert => Page.Locator("[data-testid=impacts-no-active-templates]");

    public async Task GotoAsync(int appId, string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Impacts");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    /// <summary>
    /// Declares an impact from the active template at <paramref name="templateIndex"/>
    /// (0-based, skipping the placeholder), filling its dynamic parameters. Returns false
    /// when no active templates exist.
    /// </summary>
    public async Task<bool> AddImpactAsync(int templateIndex = 0)
    {
        if (await NoActiveTemplatesAlert.CountAsync() > 0)
        {
            return false;
        }

        var options = await TemplateSelect.Locator("option").AllAsync();
        if (options.Count <= templateIndex + 1)
        {
            return false;
        }
        var value = await options[templateIndex + 1].GetAttributeAsync("value");

        await Page.RunAndWaitForResponseAsync(
            async () => await TemplateSelect.SelectOptionAsync(value!),
            r => r.Url.Contains("/Impact/TemplateParameters/"));

        var inputs = ParamsContainer.Locator("input[data-dynamic-field]");
        var count = await inputs.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var input = inputs.Nth(i);
            var type = await input.GetAttributeAsync("type");
            await input.FillAsync(type switch
            {
                "number" => "100",
                "date" => "2026-12-31",
                _ => "Valor de prueba",
            });
        }

        await AddSubmit.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/\d+/Impacts"));
        return true;
    }

    /// <summary>Declares one impact if the application has none yet.</summary>
    public async Task EnsureAtLeastOneImpactAsync(int appId, string baseUrl)
    {
        await GotoAsync(appId, baseUrl);
        if (await DeclaredImpactRows.CountAsync() == 0)
        {
            await AddImpactAsync(0);
        }
    }
}
