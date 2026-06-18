using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 039 / US4 / FR-021 — when two eligible providers tie for the highest total,
/// no provider is auto-recommended and the reviewer is told a manual selection is
/// required (SC-005).
/// </summary>
public class SupplierRecommendationTieTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"tie-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\ntie\n%%EOF\n");
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    [Test]
    public async Task TopScoreTie_NoRecommendedBadge_ManualSelectionMessageShown()
    {
        var appId = await SetupTiedAppAsync();

        var reviewerEmail = $"tie_rev_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "TieRev", "Reviewer", $"TIER-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        await Expect(firstItem.Locator(".recommended-badge")).ToHaveCountAsync(0);
        await Expect(firstItem.Locator("[data-testid=recommendation-tie]")).ToBeVisibleAsync();
    }

    private async Task<int> SetupTiedAppAsync()
    {
        var email = $"tie_app_{_uniqueId}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Tie", "Applicant", $"TIEA-{_uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Tie Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);

        // Two identical providers (same price, delivery, warranty, no statuses) → tie.
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"TIEA1-{_uniqueId}", "Proveedor Alfa", 1000m, "2027-12-31",
            _testFilePath, deliveryLeadTimeDays: 30, warrantyMonths: 12);
        await supplierPage.SubmitAsync();

        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"TIEA2-{_uniqueId}", "Proveedor Beta", 1000m, "2027-12-31",
            _testFilePath, deliveryLeadTimeDays: 30, warrantyMonths: 12);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        return appId;
    }
}
