using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 040 / US4 (T052) — admin manages per-stage checklist templates: create a template
/// with ordered items + required flags and activate it, then edit it. Verifies the admin
/// surface end to end against a real DB.
/// </summary>
[TestFixture]
[Category("ChecklistTemplateAdmin")]
public class ChecklistTemplateAdminTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    [Test]
    public async Task Admin_CreatesActivatesAndEditsChecklistTemplate()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"cl_admin_{uniqueId}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "Cl", "Admin", $"CLA-{uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        // Create a template with two items, activating on create.
        await Page.GotoAsync($"{BaseUrl}/Admin/CreateChecklist");
        var name = $"Auditoría {uniqueId}";
        await Page.Locator("[data-testid=admin-checklist-name]").FillAsync(name);
        await Page.Locator("[data-testid=admin-checklist-stage]").SelectOptionAsync("2"); // Auditor
        await Page.Locator("[data-testid=admin-checklist-item-text]").First.FillAsync("Verificación uno");
        await Page.Locator("[data-testid=admin-checklist-add-item]").ClickAsync();
        await Page.Locator("[data-testid=admin-checklist-item-text]").Nth(1).FillAsync("Verificación dos");
        await Page.Locator("[data-testid=admin-checklist-submit]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The list shows the new template, active, with 2 items.
        var row = Page.Locator("[data-testid=admin-checklist-row]", new() { HasTextString = name });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.Locator("[data-testid=admin-checklist-active]")).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync("2");

        // Edit: rename the template.
        await row.Locator("[data-testid=admin-checklist-edit]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var newName = $"Auditoría EDIT {uniqueId}";
        await Page.Locator("[data-testid=admin-checklist-name]").FillAsync(newName);
        await Page.Locator("[data-testid=admin-checklist-submit]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("[data-testid=admin-checklist-row]", new() { HasTextString = newName })).ToBeVisibleAsync();
    }
}
