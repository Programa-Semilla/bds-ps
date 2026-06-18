using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class SupplierSelectionTests : AuthenticatedTestBase
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
    public async Task ApprovalRequiresSupplierSelection_NonRecommendedAccepted()
    {
        var appId = await SetupSubmittedApplicationAsync(500m, 800m);

        var reviewerEmail = $"ss_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "SupSel", "Reviewer", $"SSLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Verify recommended badge is shown on lowest-price supplier
        var recommendedText = await firstItem.Locator(".quotation-row.table-success").TextContentAsync();
        Assert.That(recommendedText, Does.Contain(UiCopy.Recommended));

        // Approve without selecting supplier — should show error
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        // Clear the pre-selected supplier (scoring auto-selects the recommended one)
        var supplierDropdownClear = reviewPage.ItemSupplierDropdown(itemId);
        await supplierDropdownClear.SelectOptionAsync("");
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(Page.Locator(".alert-danger")).ToBeVisibleAsync();

        // Approve with non-recommended (more expensive) supplier — should succeed
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var options = await supplierDropdown.Locator("option").AllAsync();
        // Select the second supplier (more expensive, non-recommended)
        var lastOptionValue = await options[^1].GetAttributeAsync("value");
        await supplierDropdown.SelectOptionAsync(lastOptionValue!);
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);

        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Aprobado");
    }

    [Test]
    public async Task EqualScores_NoneRecommended_ManualSelectionRequired()
    {
        // Spec 039 / FR-021 — equal price + equal delivery + equal warranty + equal
        // (unset) statuses → a top-score tie, so no provider is auto-recommended.
        var appId = await SetupSubmittedApplicationAsync(1000m, 1000m);

        var reviewerEmail = $"ss_tie_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "TieSel", "Reviewer", $"TSLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;

        await Expect(firstItem.Locator(".recommended-badge")).ToHaveCountAsync(0);
        await Expect(firstItem.Locator("[data-testid=recommendation-tie]")).ToBeVisibleAsync();
    }

    private async Task<int> SetupSubmittedApplicationAsync(decimal price1, decimal price2)
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"ss_applicant_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "SupSel", "Applicant", $"LID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Supplier Selection Item", 0, "Specs for supplier test", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SS1-{_uniqueId}", "Supplier Cheap", price1, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SS2-{_uniqueId}", "Supplier Expensive", price2, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);

        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return appId;
    }
}
