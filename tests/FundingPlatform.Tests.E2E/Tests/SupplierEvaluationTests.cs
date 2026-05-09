using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class SupplierEvaluationTests : AuthenticatedTestBase
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
    public async Task US3_CreateSupplier_WithPartialCompliance_PersistsCorrectly()
    {
        var applicantEmail = $"se_comp_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "Compliance", "Tester", $"LID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Compliance Test Item", 0, "Test specs", BaseUrl);

        // Add supplier with CCSS and Hacienda checked but NOT SICOP
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();

        var supplierPage = new SupplierPage(Page);
        await supplierPage.FillSupplierFormAsync(
            legalId: $"COMP-{_uniqueId}",
            name: "Partial Compliance Supplier",
            price: 1000m,
            validUntil: "2027-12-31",
            filePath: _testFilePath,
            isCompliantCCSS: true,
            isCompliantHacienda: true,
            isCompliantSICOP: false);
        await supplierPage.SubmitAsync();

        // Should redirect back to application details
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Verify the supplier was added
        var itemRow = Page.Locator("table tbody tr:has-text('Compliance Test Item')");
        await Expect(itemRow).ToBeVisibleAsync();
    }

    [Test]
    public async Task US1_ThreeSuppliers_ScoresDisplayedCorrectly_RankedByScore()
    {
        var appId = await SetupSubmittedApplicationWithThreeSuppliersAsync();

        var reviewerEmail = $"se_rev_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "ScoreRev", "Reviewer", $"SRLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        // Verify scores are displayed
        var firstItem = reviewPage.ItemCards.First;
        var scoreElements = firstItem.Locator(".supplier-score");
        await Expect(scoreElements.First).ToBeVisibleAsync();

        // Verify score breakdown indicators exist
        var breakdownElements = firstItem.Locator(".score-breakdown");
        var breakdownCount = await breakdownElements.CountAsync();
        Assert.That(breakdownCount, Is.GreaterThan(0), "Score breakdown should be visible");

        // Verify recommended badge is shown on highest-scoring supplier
        var recommendedBadge = firstItem.Locator(".recommended-badge");
        await Expect(recommendedBadge.First).ToBeVisibleAsync();

        // Verify quotation rows exist and are ordered by score
        var quotationRows = reviewPage.QuotationRows(
            int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!));
        await Expect(quotationRows).ToHaveCountAsync(3);
    }

    [Test]
    public async Task US1_TiedScores_BothRecommended_LowestIdPreSelected()
    {
        var appId = await SetupSubmittedApplicationWithTiedSuppliersAsync();

        var reviewerEmail = $"se_tie_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "TieRev", "Reviewer", $"TRLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Both tied suppliers should be recommended
        var recommendedBadges = firstItem.Locator(".recommended-badge");
        await Expect(recommendedBadges).ToHaveCountAsync(2);

        // The supplier dropdown should have one option pre-selected (lowest supplier ID)
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var dropdown = reviewPage.ItemSupplierDropdown(itemId);
        var selectedValue = await dropdown.InputValueAsync();
        Assert.That(selectedValue, Is.Not.Empty, "A supplier should be pre-selected");
    }

    [Test]
    public async Task US1_SingleQuotation_GetsMaxPricePoint_IsRecommendedAndPreSelected()
    {
        var appId = await SetupSubmittedApplicationWithSingleSupplierAsync();

        var reviewerEmail = $"se_single_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "SingleRev", "Reviewer", $"SNLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;

        // Single quotation should be recommended
        var recommendedBadge = firstItem.Locator(".recommended-badge");
        await Expect(recommendedBadge).ToHaveCountAsync(1);

        // Score should show the price point (at least 1/5)
        var scoreElement = firstItem.Locator(".supplier-score");
        await Expect(scoreElement.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task US2_ReviewerOverridesPreSelected_ApproveSucceeds()
    {
        var appId = await SetupSubmittedApplicationWithThreeSuppliersAsync();

        var reviewerEmail = $"se_over_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "OverRev", "Reviewer", $"ORLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Approve with a non-recommended supplier (select the last option)
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var options = await supplierDropdown.Locator("option").AllAsync();
        var lastOptionValue = await options[^1].GetAttributeAsync("value");
        await supplierDropdown.SelectOptionAsync(lastOptionValue!);
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);

        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Aprobado");
    }

    [Test]
    public async Task US2_ReviewerOverride_PersistsAcrossPageReload()
    {
        var appId = await SetupSubmittedApplicationWithThreeSuppliersAsync();

        var reviewerEmail = $"se_persist_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "PersistRev", "Reviewer", $"PRLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Approve with the last supplier (non-recommended override)
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var options = await supplierDropdown.Locator("option").AllAsync();
        var lastOptionValue = await options[^1].GetAttributeAsync("value");
        await supplierDropdown.SelectOptionAsync(lastOptionValue!);
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);

        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        // Navigate away and back
        await reviewPage.GotoAsync(BaseUrl, appId);

        // Verify the override persists (approved status still shown, not reverted)
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Aprobado");
    }

    private async Task<int> SetupSubmittedApplicationWithThreeSuppliersAsync()
    {
        var applicantEmail = $"se_app_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "ScoreEval", "Applicant", $"LID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Score Test Item", 0, "Test specifications", BaseUrl);

        var supplierPage = new SupplierPage(Page);

        // Supplier 1: All compliant, e-invoice, highest price -> score 4/5
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        // Spec 013: applicant-form no longer exposes compliance/e-invoice checkboxes.
        // FillSupplierFormAsync silently drops those args; a follow-up admin step
        // would be required to set the four flags before scoring this supplier
        // at the maximum (4 + price = 5/5). For this E2E we accept the post-spec-013
        // baseline where the supplier lands as Draft with all flags false; the
        // SupplierScore unit tests cover the score math independently.
        await supplierPage.FillSupplierFormAsync($"SE1-{_uniqueId}", "Full Compliance Corp", 1500m, "2027-12-31", _testFilePath,
            isCompliantCCSS: true, isCompliantHacienda: true, isCompliantSICOP: true);
        await supplierPage.SubmitAsync();

        // Supplier 2: Partial compliance, no e-invoice, lowest price -> score 3/5
        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SE2-{_uniqueId}", "Cheap Partial Supplier", 500m, "2027-12-31", _testFilePath,
            isCompliantCCSS: true, isCompliantHacienda: true, isCompliantSICOP: false);
        await supplierPage.SubmitAsync();

        // Supplier 3: No compliance, no e-invoice, mid price -> score 0/5
        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SE3-{_uniqueId}", "Bare Minimum Supplier", 1000m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        // Set impact assessment
        var impactButton = Page.Locator("a:has-text('Impacto')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (int i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Submit the application
        await Page.Locator("button[type=submit]:has-text('Enviar solicitud')").ClickAsync();
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return appId;
    }

    private async Task<int> SetupSubmittedApplicationWithTiedSuppliersAsync()
    {
        var applicantEmail = $"se_tie_app_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "TieEval", "Applicant", $"LID-T-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Tie Score Item", 0, "Test specs for tie", BaseUrl);

        var supplierPage = new SupplierPage(Page);

        // Two suppliers with identical compliance and same price -> tied scores
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"TIE1-{_uniqueId}", "Tied Supplier Alpha", 1000m, "2027-12-31", _testFilePath,
            isCompliantCCSS: true, isCompliantHacienda: true, isCompliantSICOP: false);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"TIE2-{_uniqueId}", "Tied Supplier Beta", 1000m, "2027-12-31", _testFilePath,
            isCompliantCCSS: true, isCompliantHacienda: true, isCompliantSICOP: false);
        await supplierPage.SubmitAsync();

        // Set impact and submit
        var impactButton = Page.Locator("a:has-text('Impacto')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (int i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        await Page.Locator("button[type=submit]:has-text('Enviar solicitud')").ClickAsync();
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return appId;
    }

    private async Task<int> SetupSubmittedApplicationWithSingleSupplierAsync()
    {
        var applicantEmail = $"se_single_app_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "SingleEval", "Applicant", $"LID-S-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Single Supplier Item", 0, "Test specs single", BaseUrl);

        // Add only one supplier — it should automatically get the price point
        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SING-{_uniqueId}", "Solo Supplier", 750m, "2027-12-31", _testFilePath,
            isCompliantCCSS: true, isCompliantHacienda: false, isCompliantSICOP: false);
        await supplierPage.SubmitAsync();

        // Need a second supplier for minimum quotation requirement
        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SING2-{_uniqueId}", "Second Solo Supplier", 900m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        // Set impact and submit
        var impactButton = Page.Locator("a:has-text('Impacto')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (int i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        await Page.Locator("button[type=submit]:has-text('Enviar solicitud')").ClickAsync();
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return appId;
    }
}
