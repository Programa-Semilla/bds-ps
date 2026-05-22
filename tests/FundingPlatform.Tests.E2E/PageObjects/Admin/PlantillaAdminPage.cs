// Spec 021 — see specs/021-feedback-session-may13/tasks.md T076.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 021 / US1 / T076 — POM for the admin Plantilla catalog screens
/// (<c>/Admin/Plantillas</c>, /Create, /{id}/Edit). Mirrors
/// <see cref="AdminGroupsPage"/> shape: locators via <c>data-testid</c>.
/// </summary>
public class PlantillaAdminPage : AdminBasePage
{
    public PlantillaAdminPage(IPage page) : base(page)
    {
    }

    // ---------- Index ----------

    public ILocator AreaWrapper => Page.Locator("[data-testid=\"admin-plantillas-area\"]");
    public ILocator Table => Page.Locator("[data-testid=\"admin-plantillas-table\"]");
    public new ILocator EmptyState => Page.Locator("[data-testid=\"admin-plantillas-empty\"]");
    public ILocator FlashMessage => Page.Locator("[data-testid=\"admin-plantillas-flash\"]");
    public ILocator PlantillaRow(string name) =>
        Page.Locator("tr[data-testid^=\"admin-plantilla-row-\"]").Filter(new() { HasText = name });

    // ---------- Create / Edit ----------

    public ILocator NameInput => Page.Locator("[data-testid=\"admin-plantilla-name-input\"]");
    public ILocator NameError => Page.Locator("[data-testid=\"admin-plantilla-name-error\"]");
    public ILocator MinQuotationsInput => Page.Locator("[data-testid=\"admin-plantilla-min-quotations-input\"]");
    public ILocator CreateSubmit => Page.Locator("[data-testid=\"admin-plantilla-create-submit\"]");
    public ILocator EditSubmit => Page.Locator("[data-testid=\"admin-plantilla-edit-submit\"]");
    public ILocator SnapshotBanner => Page.Locator("[data-testid=\"admin-plantilla-snapshot-banner\"]");

    public ILocator ImpactTemplateCheckbox(int impactTemplateId) =>
        Page.Locator($"[data-testid=\"admin-plantilla-impact-template-{impactTemplateId}\"]");

    /// <summary>
    /// One required-field checkbox in the "Campos requeridos" group, keyed by its
    /// bit value (1 = Nombre del responsable, 2 = Teléfono, 4 = Dirección,
    /// 8 = Detalle de impacto).
    /// </summary>
    public ILocator RequiredFieldCheckbox(long bit) =>
        Page.Locator($"[data-testid=\"admin-plantilla-required-{bit}\"]");

    // ---------- Navigation ----------

    public Task GoToIndexAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Plantillas");

    public Task GoToCreateAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Plantillas/Create");

    public Task GoToEditAsync(string baseUrl, int id) =>
        Page.GotoAsync($"{baseUrl}/Admin/Plantillas/{id}/Edit");

    /// <summary>
    /// Fills the Create form with the supplied values and submits. The caller
    /// is responsible for ensuring at least one ImpactTemplate checkbox is
    /// selected (we always pick the first available one when none specified
    /// because <c>Plantilla.AssignTo</c> later requires ≥ 1).
    /// </summary>
    public async Task CreatePlantillaAsync(string name, int minimumQuotationsPerItem, IEnumerable<int>? impactTemplateIds = null)
    {
        await NameInput.FillAsync(name);
        await MinQuotationsInput.FillAsync(minimumQuotationsPerItem.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var ids = impactTemplateIds?.ToList() ?? new List<int>();
        if (ids.Count == 0)
        {
            // No ids supplied — pick the first checkbox in the multi-select.
            var first = Page.Locator("[data-testid^=\"admin-plantilla-impact-template-\"]").First;
            if (await first.CountAsync() > 0)
            {
                await first.CheckAsync();
            }
        }
        else
        {
            foreach (var id in ids)
            {
                await ImpactTemplateCheckbox(id).CheckAsync();
            }
        }

        await CreateSubmit.ClickAsync();
    }

    /// <summary>
    /// On the Edit form, sets a new minimum-quotations value and submits.
    /// Used by SC-002 snapshot-independence assertions.
    /// </summary>
    public async Task EditMinimumQuotationsAsync(int newValue)
    {
        await MinQuotationsInput.FillAsync(newValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await EditSubmit.ClickAsync();
    }
}
