using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 035 / US1 / T032 — admin configures a category's field set end-to-end
/// (create with fields of multiple data types, persist, re-open Edit to confirm
/// round-trip + sort order).
/// </summary>
public class CategoryFieldAdminTests : AuthenticatedTestBase
{
    private async Task RegisterAndLoginAsAdminAsync(string email, string password)
    {
        await RegisterUserAsync(Page, email, password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);

        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        var token = await Page.Locator("input[name='__RequestVerificationToken']").GetAttributeAsync("value");
        var formData = Page.APIRequest.CreateFormData();
        formData.Set("email", email);
        formData.Set("__RequestVerificationToken", token ?? "");
        var response = await Page.APIRequest.PostAsync($"{BaseUrl}/Account/PromoteToAdmin",
            new APIRequestContextOptions { Form = formData });
        Assert.That(response.Ok, Is.True, "Failed to promote user to admin");

        await LoginAsync(Page, email, password);
    }

    private async Task FillFieldAsync(int idx, string name, string label, string dataType, bool required, int order)
    {
        await Page.Locator($"input[name='Fields[{idx}].Name']").FillAsync(name);
        await Page.Locator($"input[name='Fields[{idx}].DisplayLabel']").FillAsync(label);
        await Page.Locator($"select[name='Fields[{idx}].DataType']").SelectOptionAsync(dataType);
        if (required)
        {
            await Page.Locator($"input[name='Fields[{idx}].IsRequired']").CheckAsync();
        }
        await Page.Locator($"input[name='Fields[{idx}].SortOrder']").FillAsync(order.ToString());
    }

    [Test]
    public async Task CreateCategory_WithFields_PersistsAndRoundTrips()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin_cat_{uniqueId}@example.com";
        var categoryName = $"Equipo {uniqueId}";

        await RegisterAndLoginAsAdminAsync(email, "Test123!");

        // Go to the categories admin list and create a new category.
        await Page.GotoAsync($"{BaseUrl}/Admin/Categories");
        await Page.Locator("[data-testid='admin-category-create']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/CreateCategory"));

        await Page.Locator("[data-testid='admin-category-name-input']").FillAsync(categoryName);

        // Add two fields of different data types (one required).
        await Page.Locator("[data-testid='admin-category-add-field']").ClickAsync();
        await FillFieldAsync(0, "marca", "Marca", "Text", required: false, order: 1);
        await Page.Locator("[data-testid='admin-category-add-field']").ClickAsync();
        await FillFieldAsync(1, "costo", "Costo unitario", "Decimal", required: true, order: 2);

        await Page.Locator("[data-testid='admin-category-save']").ClickAsync();

        // Back on the list with a success toast/alert and the new row (2 fields).
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Categories"));
        var row = Page.Locator($"table tbody tr:has-text('{categoryName}')");
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.Locator("td:nth-child(3)")).ToHaveTextAsync("2");

        // Re-open Edit and confirm the fields round-tripped in sort order.
        await row.Locator("[data-testid='admin-category-edit']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/EditCategory"));
        await Expect(Page.Locator("input[name='Fields[0].DisplayLabel']")).ToHaveValueAsync("Marca");
        await Expect(Page.Locator("input[name='Fields[1].DisplayLabel']")).ToHaveValueAsync("Costo unitario");
        await Expect(Page.Locator("select[name='Fields[1].DataType']")).ToHaveValueAsync("Decimal");
    }

    private async Task CreateCategoryAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/Admin/CreateCategory");
        await Page.Locator("[data-testid='admin-category-name-input']").FillAsync(name);
        await Page.Locator("[data-testid='admin-category-save']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Categories"));
    }

    [Test]
    public async Task CategoriesList_HasTitleAndFiltersByNameAndStatus()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin_catf_{uniqueId}@example.com";
        var activeName = $"Alfa {uniqueId}";
        var inactiveName = $"Beta {uniqueId}";

        await RegisterAndLoginAsAdminAsync(email, "Test123!");

        await CreateCategoryAsync(activeName);
        await CreateCategoryAsync(inactiveName);

        // Deactivate the second category via its Edit form.
        var inactiveRow = Page.Locator($"table tbody tr:has-text('{inactiveName}')");
        await inactiveRow.Locator("[data-testid='admin-category-edit']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/EditCategory"));
        await Page.Locator("[data-testid='admin-category-active']").UncheckAsync();
        await Page.Locator("[data-testid='admin-category-save']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Categories"));

        // Title reflects the renamed page.
        await Expect(Page.Locator("[data-testid='page-title']")).ToHaveTextAsync("Plantillas de Categorías");

        var activeRow = Page.Locator($"table tbody tr:has-text('{activeName}')");
        inactiveRow = Page.Locator($"table tbody tr:has-text('{inactiveName}')");

        // Search by name narrows to the matching row.
        await Page.Locator("[data-testid='admin-category-filter-search']").FillAsync(inactiveName);
        await Expect(inactiveRow).ToBeVisibleAsync();
        await Expect(activeRow).ToBeHiddenAsync();

        // Clearing the search restores both, then filter by status = Activa.
        await Page.Locator("[data-testid='admin-category-filter-search']").FillAsync("");
        await Page.Locator("[data-testid='admin-category-filter-status']").SelectOptionAsync("active");
        await Expect(activeRow).ToBeVisibleAsync();
        await Expect(inactiveRow).ToBeHiddenAsync();

        // Status = Inactiva shows only the deactivated category.
        await Page.Locator("[data-testid='admin-category-filter-status']").SelectOptionAsync("inactive");
        await Expect(inactiveRow).ToBeVisibleAsync();
        await Expect(activeRow).ToBeHiddenAsync();
    }
}
