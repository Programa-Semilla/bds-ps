using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

// Spec 021 / US2 — a draft is created and edited entirely on the draft editor
// (/Application/Edit/{id}); this test verifies a draft (and its items) survives
// a logout / login round-trip.
public class DraftPersistenceTests : AuthenticatedTestBase
{
    [Test]
    public async Task SaveDraft_And_ReturnLater()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"draft_persist_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Draft", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        // Create a draft — opens the draft editor.
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        // Add an item inline.
        var draft = new ApplicationDraftPage(Page);
        await draft.AddItemAsync("Persisted Laptop", "16GB RAM, 512GB SSD");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        await Expect(draft.ItemRows.Filter(new() { HasTextString = "Persisted Laptop" })).ToBeVisibleAsync();

        // Log out, then log back in.
        await Page.Locator($"button:has-text('{UiCopy.Logout}')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/$|/Home"));
        await LoginAsync(Page, email, password);

        // The draft appears in the list as a Borrador, with an Editar link.
        await appPage.GotoListAsync(BaseUrl);
        var appRow = Page.Locator($"table tbody tr:has(a[href*='Application/Edit/{appId}'])");
        await Expect(appRow).ToBeVisibleAsync();
        await Expect(appRow.Locator("[data-testid=status-pill]:has-text('Borrador')")).ToBeVisibleAsync();

        // Reopen the draft — the item is still there.
        await appRow.Locator("a:has-text('Editar')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        await Expect(draft.ItemRows.Filter(new() { HasTextString = "Persisted Laptop" })).ToBeVisibleAsync();
    }
}
