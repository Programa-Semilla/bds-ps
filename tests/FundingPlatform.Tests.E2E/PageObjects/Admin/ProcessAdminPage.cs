// Spec 021 — see specs/021-feedback-session-may13/tasks.md T076.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 021 / US1 / T076 — POM for the admin Process catalog screens
/// (<c>/Admin/Processes</c>, /Create, /{id}). Mirrors the spec-016
/// <see cref="AdminGroupsPage"/> shape: locators by <c>data-testid</c> only,
/// per-form helper methods for the most common flows.
/// </summary>
public class ProcessAdminPage : AdminBasePage
{
    public ProcessAdminPage(IPage page) : base(page)
    {
    }

    // ---------- Index ----------

    public ILocator AreaWrapper => Page.Locator("[data-testid=\"admin-processes-area\"]");
    public ILocator Table => Page.Locator("[data-testid=\"admin-processes-table\"]");
    public new ILocator EmptyState => Page.Locator("[data-testid=\"admin-processes-empty\"]");
    public ILocator FlashMessage => Page.Locator("[data-testid=\"admin-processes-flash\"]");
    public ILocator ProcessRow(string name) =>
        Page.Locator("tr[data-testid^=\"admin-process-row-\"]").Filter(new() { HasText = name });

    // ---------- Create ----------

    public ILocator NameInput => Page.Locator("[data-testid=\"admin-process-name-input\"]");
    public ILocator NameError => Page.Locator("[data-testid=\"admin-process-name-error\"]");
    public ILocator CreateSubmit => Page.Locator("[data-testid=\"admin-process-create-submit\"]");

    // ---------- Details ----------

    public ILocator DetailsArea => Page.Locator("[data-testid=\"admin-process-details-area\"]");
    public ILocator PlantillaCard => Page.Locator("[data-testid=\"admin-process-plantilla-card\"]");
    public ILocator PlantillaSnapshot => Page.Locator("[data-testid=\"admin-process-plantilla-snapshot\"]");
    public ILocator PlantillaBaseName => Page.Locator("[data-testid=\"admin-process-plantilla-base-name\"]");
    public ILocator PlantillaMinQuotations => Page.Locator("[data-testid=\"admin-process-plantilla-min-quotations\"]");
    public ILocator PlantillaImpacts => Page.Locator("[data-testid=\"admin-process-plantilla-impacts\"]");
    public ILocator AssignPlantillaForm => Page.Locator("[data-testid=\"admin-process-assign-plantilla-form\"]");
    public ILocator AssignPlantillaSelect => Page.Locator("[data-testid=\"admin-process-assign-plantilla-select\"]");
    public ILocator AssignPlantillaSubmit => Page.Locator("[data-testid=\"admin-process-assign-plantilla-submit\"]");

    public ILocator GroupsCard => Page.Locator("[data-testid=\"admin-process-groups-card\"]");
    public ILocator GroupRow(string name) =>
        Page.Locator("tr[data-testid^=\"admin-process-group-row-\"]").Filter(new() { HasText = name });

    // ---------- Navigation ----------

    public Task GoToIndexAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Processes");

    public Task GoToCreateAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Processes/Create");

    public Task GoToDetailsAsync(string baseUrl, int id) =>
        Page.GotoAsync($"{baseUrl}/Admin/Processes/{id}");

    public async Task CreateProcessAsync(string name)
    {
        await NameInput.FillAsync(name);
        await CreateSubmit.ClickAsync();
    }

    public async Task AssignPlantillaAsync(string plantillaOptionTextFragment)
    {
        // The select option text reads e.g. "PlantillaMVP-v1 (mín. 3 cotizaciones, 1 plantilla(s) de impacto)".
        // We pick by label substring so the test stays robust against rendering drift.
        var labels = await AssignPlantillaSelect.Locator("option").AllTextContentsAsync();
        var match = labels.FirstOrDefault(l => l.Contains(plantillaOptionTextFragment, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Plantilla option containing '{plantillaOptionTextFragment}' not found. Available: {string.Join(" | ", labels)}");
        await AssignPlantillaSelect.SelectOptionAsync(new SelectOptionValue { Label = match });
        await AssignPlantillaSubmit.ClickAsync();
    }
}
