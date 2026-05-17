using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class ItemManagementTests : AuthenticatedTestBase
{
    [Test]
    public async Task CreateApplication_And_AddItem()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"item_test_{uniqueId}@example.com";
        var password = "Test123!";

        // Register and login
        await RegisterUserAsync(Page, email, password, "Item", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        // Navigate to applications and create one
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        // Should be on the draft editor page
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Extract application ID from URL
        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True, "Should be on the draft editor page with ID");

        // Add an item
        var itemPage = new ItemPage(Page);
        var appId = int.Parse(appIdMatch.Groups[1].Value);
        await itemPage.AddItemAsync(appId, "Test Laptop", 0, "Intel i7, 16GB RAM, 512GB SSD", BaseUrl);

        // Should redirect back to the draft editor
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Verify item appears in the editor's item rows
        var itemRow = Page.Locator("[data-testid=application-edit-item-row]:has-text('Test Laptop')");
        await Expect(itemRow).ToBeVisibleAsync();
    }

    [Test]
    public async Task EditItem_UpdatesSuccessfully()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"edit_test_{uniqueId}@example.com";
        var password = "Test123!";

        // Register and login
        await RegisterUserAsync(Page, email, password, "Edit", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        // Create application
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Add an item
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Original Product", 0, "Original specs", BaseUrl);

        // Find the edit button for the item in the draft editor and click it
        var editButton = Page.Locator("[data-testid=application-edit-item-row] a:has-text('Editar')").First;
        await editButton.ClickAsync();

        // Edit the item
        await itemPage.ProductNameInput.ClearAsync();
        await itemPage.ProductNameInput.FillAsync("Updated Product");
        await itemPage.TechnicalSpecificationsInput.ClearAsync();
        await itemPage.TechnicalSpecificationsInput.FillAsync("Updated specs");
        await itemPage.SubmitButton.ClickAsync();

        // Should redirect back to the draft editor
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Verify updated item appears
        var updatedRow = Page.Locator("[data-testid=application-edit-item-row]:has-text('Updated Product')");
        await Expect(updatedRow).ToBeVisibleAsync();

        // Verify original name is gone
        var originalRow = Page.Locator("[data-testid=application-edit-item-row]:has-text('Original Product')");
        await Expect(originalRow).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task RemoveItem_DeletesFromApplication()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"delete_test_{uniqueId}@example.com";
        var password = "Test123!";

        // Register and login
        await RegisterUserAsync(Page, email, password, "Delete", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        // Create application
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Add an item
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Item To Delete", 0, "Will be removed", BaseUrl);

        // Verify item exists in the draft editor
        var itemRow = Page.Locator("[data-testid=application-edit-item-row]:has-text('Item To Delete')");
        await Expect(itemRow).ToBeVisibleAsync();

        // Click delete button and handle confirmation dialog
        Page.Dialog += (_, dialog) => dialog.AcceptAsync();
        var deleteButton = Page.Locator($"[data-testid=application-edit-item-row] button:has-text('{UiCopy.Delete}')").First;
        await deleteButton.ClickAsync();

        // Should redirect back to the draft editor
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Verify item is gone
        var deletedRow = Page.Locator("[data-testid=application-edit-item-row]:has-text('Item To Delete')");
        await Expect(deletedRow).Not.ToBeVisibleAsync();
    }
}
