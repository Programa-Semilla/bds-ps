using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 039 / US1 — the seven-criterion explainable recommendation. A higher-priced
/// provider with shorter delivery + longer warranty is recommended over the cheapest
/// (SC-001), and the full per-criterion breakdown + raw values render (SC-002).
/// </summary>
public class SupplierRecommendationTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test-quotation-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Test quotation document content");
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    [Test]
    public async Task NonCheapestProvider_WithBetterDeliveryAndWarranty_IsRecommended_WithBreakdown()
    {
        var appId = await SetupAppWithTwoSuppliersAsync();

        var reviewerEmail = $"sr_rev_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "RecRev", "Reviewer", $"SRR-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;

        // The pricier-but-better provider (Premium) is Recomendada; the cheap one is not.
        var premiumRow = firstItem.Locator("[data-testid=review-quotation-row]")
            .Filter(new() { HasText = "Premium" });
        var cheapRow = firstItem.Locator("[data-testid=review-quotation-row]")
            .Filter(new() { HasText = "Barato" });

        await Expect(premiumRow.Locator(".recommended-badge")).ToHaveCountAsync(1);
        await Expect(cheapRow.Locator(".recommended-badge")).ToHaveCountAsync(0);

        // The seven-criterion breakdown + raw values render for the recommended provider.
        var breakdown = premiumRow.Locator("[data-testid=score-breakdown]");
        await Expect(breakdown).ToBeVisibleAsync();
        foreach (var label in new[] { "Precio", "Entrega", "Garantía", "Hacienda", "CCSS", "SICOP", "PYME" })
        {
            await Expect(breakdown).ToContainTextAsync(label);
        }
        // Raw delivery/warranty values are shown (10 días / 24 meses for Premium).
        await Expect(breakdown).ToContainTextAsync("10 días");
        await Expect(breakdown).ToContainTextAsync("24 meses");

        // SC-002 — the breakdown renders for EACH eligible provider, with that
        // provider's own (discriminating) raw values, not just the recommended one.
        var cheapBreakdown = cheapRow.Locator("[data-testid=score-breakdown]");
        await Expect(cheapBreakdown).ToBeVisibleAsync();
        await Expect(cheapBreakdown).ToContainTextAsync("60 días");
        await Expect(cheapBreakdown).ToContainTextAsync("6 meses");

        // The total is shown for both (not the old /4 fraction).
        await Expect(premiumRow.Locator("[data-testid=quotation-total]")).ToBeVisibleAsync();
        await Expect(cheapRow.Locator("[data-testid=quotation-total]")).ToBeVisibleAsync();
    }

    private async Task<int> SetupAppWithTwoSuppliersAsync()
    {
        var applicantEmail = $"sr_app_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "RecEval", "Applicant", $"SRA-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Recommendation Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);

        // Cheap provider: lowest price, but slow delivery + short warranty.
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync(
            $"BARATO-{_uniqueId}", "Proveedor Barato", price: 500m, validUntil: "2027-12-31",
            filePath: _testFilePath, deliveryLeadTimeDays: 60, warrantyMonths: 6);
        await supplierPage.SubmitAsync();

        // Premium provider: higher price, but fast delivery + long warranty → higher total.
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync(
            $"PREMIUM-{_uniqueId}", "Proveedor Premium", price: 900m, validUntil: "2027-12-31",
            filePath: _testFilePath, deliveryLeadTimeDays: 10, warrantyMonths: 24);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        return appId;
    }
}
