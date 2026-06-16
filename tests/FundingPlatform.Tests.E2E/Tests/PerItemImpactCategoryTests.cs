using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 035 (evolved 2026-06-16) / US2+US3 / TE020+TE026 — the applicant declares the
/// application's impacts, then captures a line item through the category-first form and
/// attributes it to one or more declared impacts with a short justification. Covers the
/// golden path (declare → attribute → gate opens) and submit-blocked-on-missing-attribution.
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
    public async Task GoldenPath_DeclareImpact_ThenAttributeLineItem_OpensGate()
    {
        var appId = await CreateDraftAsync("pic_golden");

        // (1) Declare an impact at the application level.
        var impactsPage = new ApplicationImpactsPage(Page);
        await impactsPage.GotoAsync(appId, BaseUrl);
        Assert.That(await impactsPage.AddImpactAsync(0), Is.True, "an active impact template should exist in the seed");
        await Expect(impactsPage.DeclaredImpactRows).ToHaveCountAsync(1);

        // (2) Category-first add + attribute the line to the declared impact + justify.
        var itemPage = new ItemPage(Page);
        await Page.GotoAsync($"{BaseUrl}/Application/{appId}/Item/Add");
        await itemPage.SelectCategoryAndFillFieldsAsync(0);
        await Expect(itemPage.CategoryFieldsContainer.Locator("input[data-dynamic-field]").First)
            .ToBeVisibleAsync();
        await itemPage.ProductNameInput.FillAsync("Laptop de desarrollo");

        // The attribution checkboxes are present (impacts were declared).
        await Expect(itemPage.ImpactAttributionOptions.First).ToBeVisibleAsync();
        Assert.That(await itemPage.AttributeFirstImpactAndJustifyAsync(), Is.True);
        await itemPage.SubmitButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // The item row shows the "Impacto" badge and the submit gate is OPEN.
        await Expect(Page.Locator("[data-testid^=item-impact-ok-]").First).ToBeVisibleAsync();
        var draft = new ApplicationDraftPage(Page);
        await Expect(draft.ItemRows).ToHaveCountAsync(1);
        await Expect(draft.SubmitButton).ToBeEnabledAsync();
    }

    [Test]
    public async Task ItemWithoutAttribution_KeepsSubmitGateClosed()
    {
        var appId = await CreateDraftAsync("pic_noimpact");

        // Declare an impact so attribution is possible, but add an item WITHOUT attributing.
        var impactsPage = new ApplicationImpactsPage(Page);
        await impactsPage.GotoAsync(appId, BaseUrl);
        await impactsPage.AddImpactAsync(0);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Servidor", 0, "Specs", BaseUrl, withImpact: false);

        // The item row flags impact as pending and the submit gate stays CLOSED.
        await Expect(Page.Locator("[data-testid^=item-impact-missing-]").First).ToBeVisibleAsync();
        var draft = new ApplicationDraftPage(Page);
        await Expect(draft.ItemRows).ToHaveCountAsync(1);
        await Expect(draft.SubmitButton).ToBeDisabledAsync();
    }

    [Test]
    public async Task RemoveDeclaredImpact_StripsAttribution()
    {
        var appId = await CreateDraftAsync("pic_remove");

        var impactsPage = new ApplicationImpactsPage(Page);
        await impactsPage.GotoAsync(appId, BaseUrl);
        await impactsPage.AddImpactAsync(0);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Equipo", 0, "Specs", BaseUrl, withImpact: true);
        await Expect(Page.Locator("[data-testid^=item-impact-ok-]").First).ToBeVisibleAsync();

        // Remove the declared impact — the line item's attribution is stripped (SC-007),
        // so the submit gate closes again.
        await impactsPage.GotoAsync(appId, BaseUrl);
        await impactsPage.DeclaredImpactRows.First.Locator("[data-testid=declared-impact-remove]").ClickAsync();
        await Expect(impactsPage.EmptyState).ToBeVisibleAsync();

        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        await Expect(Page.Locator("[data-testid^=item-impact-missing-]").First).ToBeVisibleAsync();
    }
}
