using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 039 / US3 — a CCSS sin inscripción provider is excluded from scoring, shown
/// bloqueado, and an item cannot be approved while it is selected (FR-016..FR-020,
/// SC-003). All-blocked items show "ningún proveedor elegible".
///
/// The recommendation + gate read the provider's CURRENT CCSS status live, so the
/// applicant creates the providers (eligible) and submits; an auditor then sets the
/// CCSS status; the reviewer surface reflects it on the next render.
/// </summary>
public class SupplierRecommendationBlockTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"block-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\nblock\n%%EOF\n");
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    [Test]
    public async Task BlockedProvider_ExcludedAndFlagged_ApproveGatedUntilEligibleSelected()
    {
        var blockedName = $"Proveedor Bloqueado {_uniqueId}";
        var eligibleName = $"Proveedor Elegible {_uniqueId}";
        var appId = await CreateSubmittedAppAsync(blockedName, eligibleName);

        // Auditor marks the first provider CCSS sin inscripción.
        await SetCcssSinInscripcionByNameAsync(blockedName);
        var blockedId = await SupplierSeed.GetSupplierIdByNameAsync(ConnectionString, blockedName);
        var eligibleId = await SupplierSeed.GetSupplierIdByNameAsync(ConnectionString, eligibleName);

        var reviewerEmail = $"blk_rev_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "BlkRev", "Reviewer", $"BLR-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // The blocked provider is flagged; the eligible one is recommended.
        await Expect(firstItem.Locator("[data-testid=blocked-supplier-badge]")).ToHaveCountAsync(1);
        var eligibleRow = firstItem.Locator("[data-testid=review-quotation-row]")
            .Filter(new() { HasText = "Elegible" });
        await Expect(eligibleRow.Locator(".recommended-badge")).ToHaveCountAsync(1);

        // Approving with the blocked provider is rejected with the es-CR message.
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        await reviewPage.ItemSupplierDropdown(itemId).SelectOptionAsync(blockedId.ToString());
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(reviewPage.ErrorMessage).ToContainTextAsync("no está inscrito en la CCSS");

        // Switching to the eligible provider lets the approval through.
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        await reviewPage.ItemSupplierDropdown(itemId).SelectOptionAsync(eligibleId.ToString());
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(reviewPage.SuccessMessage).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Aprobado");
    }

    [Test]
    public async Task AllProvidersBlocked_ItemShowsNoEligibleProvider()
    {
        var name1 = $"Bloqueado Uno {_uniqueId}";
        var name2 = $"Bloqueado Dos {_uniqueId}";
        var appId = await CreateSubmittedAppAsync(name1, name2);

        await SetCcssSinInscripcionByNameAsync(name1, name2);

        var reviewerEmail = $"blk2_rev_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Blk2Rev", "Reviewer", $"BL2R-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        await Expect(firstItem.Locator("[data-testid=no-eligible-supplier]")).ToBeVisibleAsync();
        await Expect(firstItem.Locator(".recommended-badge")).ToHaveCountAsync(0);
    }

    private async Task SetCcssSinInscripcionByNameAsync(params string[] supplierNames)
    {
        var sfx = Guid.NewGuid().ToString("N")[..8];
        var auditorEmail = $"blk_aud_{sfx}@example.com";
        await RegisterUserAsync(Page, auditorEmail, "Test123!", "Blk", "Auditor", $"BLA-{sfx}");
        await AssignRoleAsync(auditorEmail, "Auditor");
        await LoginAsync(Page, auditorEmail, "Test123!");

        foreach (var name in supplierNames)
        {
            var id = await SupplierSeed.GetSupplierIdByNameAsync(ConnectionString, name);
            await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{id}");
            // CCSS sin inscripción = code 1.
            await Page.Locator("[data-testid=\"admin-supplier-ccss-select\"]").SelectOptionAsync("1");
            await Page.Locator("[data-testid=\"admin-supplier-edit-submit\"]").ClickAsync();
            // Re-read to confirm persistence before moving on.
            await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{id}");
            await Expect(Page.Locator("[data-testid=\"admin-supplier-ccss-select\"]")).ToHaveValueAsync("1");
        }

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    private async Task<int> CreateSubmittedAppAsync(string name1, string name2)
    {
        var email = $"blk_app_{_uniqueId}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Blk", "Applicant", $"BLAP-{_uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Block Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);

        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"3-101-A{_uniqueId}", name1, 500m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"3-101-B{_uniqueId}", name2, 900m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        return appId;
    }
}
