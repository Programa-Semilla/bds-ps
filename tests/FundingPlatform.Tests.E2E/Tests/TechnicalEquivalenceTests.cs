using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class TechnicalEquivalenceTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test-quotation-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Test quotation document content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    [Test]
    public async Task FlagNotEquivalent_TriggersAutoRejection_ClearFlagReturnsToPending()
    {
        var appId = await SetupSubmittedApplicationAsync();

        var reviewerEmail = $"te_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "TechEq", "Reviewer", $"TELID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Flag as not equivalent
        await reviewPage.TechnicalEquivalenceSubmit(itemId).ClickAsync();

        // Verify auto-rejection
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Rechazado");

        // Verify item is flagged (the flag warning should be visible)
        var flagWarning = Page.Locator($".review-item[data-item-id='{itemId}'] .alert-warning:has-text('no son técnicamente equivalentes')");
        await Expect(flagWarning).ToBeVisibleAsync();

        // Clear the flag
        await reviewPage.TechnicalEquivalenceSubmit(itemId).ClickAsync();

        // Verify returns to Pending
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Pendiente");
    }

    private async Task<int> SetupSubmittedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"te_applicant_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "TechEq", "Applicant", $"LID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Tech Equivalence Item", 0, "Various specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"TE1-{_uniqueId}", "Supplier One", 600m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"TE2-{_uniqueId}", "Supplier Two", 800m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);

        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return appId;
    }
}
