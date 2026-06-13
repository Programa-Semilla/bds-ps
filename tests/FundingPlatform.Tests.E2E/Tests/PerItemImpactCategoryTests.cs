using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 035 / US2 / T040 — applicant captures a line item through the category-first
/// form: pick category → dynamic category fields → product name → per-item impact
/// template + parameters. Covers the golden path (saves + opens the submit gate) and
/// submit-blocked-on-missing-impact (an impact-pending item keeps the gate closed).
/// </summary>
public class PerItemImpactCategoryTests : AuthenticatedTestBase
{
    private async Task<int> CreateDraftAsync(string emailPrefix)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"{emailPrefix}_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Cat", "Tester", $"CID-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        return int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
    }

    [Test]
    public async Task GoldenPath_CategoryFields_AndPerItemImpact_SavesAndOpensGate()
    {
        var appId = await CreateDraftAsync("pic_golden");

        // Category-first add: category (index 0 — has required fields in the seed),
        // its dynamic fields, product name, then a per-item impact template + params.
        var itemPage = new ItemPage(Page);
        await Page.GotoAsync($"{BaseUrl}/Application/{appId}/Item/Add");
        await itemPage.SelectCategoryAndFillFieldsAsync(0);

        // The dynamic category fields were rendered (the seed's first category
        // carries several) and the product name input is present.
        await Expect(itemPage.CategoryFieldsContainer.Locator("input[data-dynamic-field]").First)
            .ToBeVisibleAsync();
        await itemPage.ProductNameInput.FillAsync("Laptop de desarrollo");

        await itemPage.SelectImpactAndFillAsync();
        await itemPage.SubmitButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // The item row shows the "Impacto" badge and the submit gate is OPEN.
        await Expect(Page.Locator("[data-testid^=item-impact-ok-]").First).ToBeVisibleAsync();
        var draft = new ApplicationDraftPage(Page);
        await Expect(draft.ItemRows).ToHaveCountAsync(1);
        await Expect(draft.SubmitButton).ToBeEnabledAsync();
    }

    [Test]
    public async Task ItemWithoutImpact_KeepsSubmitGateClosed()
    {
        var appId = await CreateDraftAsync("pic_noimpact");

        // Add an item with category fields + product name but NO impact template.
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Servidor", 0, "Specs", BaseUrl, withImpact: false);

        // The item row flags impact as pending and the submit gate stays CLOSED.
        await Expect(Page.Locator("[data-testid^=item-impact-missing-]").First).ToBeVisibleAsync();
        var draft = new ApplicationDraftPage(Page);
        await Expect(draft.ItemRows).ToHaveCountAsync(1);
        await Expect(draft.SubmitButton).ToBeDisabledAsync();
    }
}
