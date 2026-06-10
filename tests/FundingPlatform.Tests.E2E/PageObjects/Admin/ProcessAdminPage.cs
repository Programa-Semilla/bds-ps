// Spec 021 — see specs/021-feedback-session-may13/tasks.md T076.

using System.Text.RegularExpressions;
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
    // Spec 024 — flash messages now surface as toasts (success-banner / error-banner preserved).
    public ILocator FlashMessage => Page.Locator("[data-testid=\"success-banner\"]");
    public ILocator ProcessRow(string name) =>
        Page.Locator("tr[data-testid^=\"admin-process-row-\"]").Filter(new() { HasText = name });

    // ---------- Create ----------

    public ILocator NameInput => Page.Locator("[data-testid=\"admin-process-name-input\"]");
    // Spec 029 / FR-002 — the Process create form now requires a Fund.
    public ILocator FundSelect => Page.Locator("[data-testid=\"admin-process-fund-select\"]");
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

    // Spec 021 / US1 — detach the ProcessPlantilla snapshot so a different
    // base Plantilla can be assigned. Detach posts to AdminPlantillasController.
    public ILocator DetachPlantillaForm => Page.Locator("[data-testid=\"admin-process-detach-plantilla-form\"]");
    public ILocator DetachPlantillaSubmit => Page.Locator("[data-testid=\"admin-process-detach-plantilla-submit\"]");

    public ILocator GroupsCard => Page.Locator("[data-testid=\"admin-process-groups-card\"]");
    public ILocator GroupRow(string name) =>
        Page.Locator("tr[data-testid^=\"admin-process-group-row-\"]").Filter(new() { HasText = name });

    // Spec 021 / FR-001 — Groups are created from the Process detail page.
    public ILocator GroupCreateForm => Page.Locator("[data-testid=\"admin-process-group-create-form\"]");
    public ILocator GroupNameInput => Page.Locator("[data-testid=\"admin-process-group-name-input\"]");
    public ILocator GroupCreateSubmit => Page.Locator("[data-testid=\"admin-process-group-create-submit\"]");
    public ILocator FlashMessageDetail => Page.Locator("[data-testid=\"success-banner\"]");
    public ILocator FlashError => Page.Locator("[data-testid=\"error-banner\"]");

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
        // Spec 029 / FR-002 — anchor the Process to the first Active Fund
        // (the seed "Fondo General" is always present).
        if (await FundSelect.CountAsync() > 0)
        {
            await FundSelect.SelectOptionAsync(new Microsoft.Playwright.SelectOptionValue { Index = 1 });
        }
        await CreateSubmit.ClickAsync();
    }

    /// <summary>
    /// Spec 021 / FR-001 — opens /Admin/Processes, locates the row for
    /// <paramref name="name"/>, and navigates to its detail page. Returns the
    /// parsed Process id.
    /// </summary>
    public async Task<int> OpenProcessDetailByNameAsync(string baseUrl, string name)
    {
        await GoToIndexAsync(baseUrl);
        var testid = await ProcessRow(name).GetAttributeAsync("data-testid")
            ?? throw new InvalidOperationException($"Process row '{name}' not found on /Admin/Processes.");
        var id = int.Parse(Regex.Match(testid, @"admin-process-row-(\d+)$").Groups[1].Value);
        await GoToDetailsAsync(baseUrl, id);
        return id;
    }

    /// <summary>Spec 021 / FR-001 — creates a Group under the currently-open
    /// Process detail page via the inline "Nuevo grupo" form.</summary>
    public async Task CreateGroupAsync(string name)
    {
        await GroupNameInput.FillAsync(name);
        await GroupCreateSubmit.ClickAsync();
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

    /// <summary>
    /// Spec 021 / US1 — clicks "Desasignar plantilla" on the Process detail and
    /// accepts the JS confirm dialog. After this the assign form is shown again.
    /// </summary>
    public async Task DetachPlantillaAsync()
    {
        // Spec 024 — detach now opens the shared confirm modal; click confirm to proceed.
        await DetachPlantillaSubmit.ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
    }
}
