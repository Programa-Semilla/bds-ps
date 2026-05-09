using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class ReviewItemDecisionTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

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
    public async Task ReviewItem_ApproveWithSupplierAndComment()
    {
        var (appId, _) = await SetupSubmittedApplicationAsync();

        // Login as reviewer and open review
        var reviewerEmail = $"rid_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Decision", "Reviewer", $"RLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        // Verify Under Review state
        await Expect(reviewPage.ApplicationState).ToContainTextAsync("En revisión");

        // Get the item ID from the first review item
        var firstItem = reviewPage.ItemCards.First;
        var itemId = await firstItem.GetAttributeAsync("data-item-id");
        Assert.That(itemId, Is.Not.Null);
        var id = int.Parse(itemId!);

        // Select Approve radio
        await reviewPage.ItemDecisionRadio(id, "Approve").CheckAsync();

        // Select a supplier from the dropdown
        var supplierDropdown = reviewPage.ItemSupplierDropdown(id);
        await Expect(supplierDropdown).ToBeVisibleAsync();
        var options = await supplierDropdown.Locator("option").AllAsync();
        Assert.That(options.Count, Is.GreaterThan(1));
        var firstSupplierValue = await options[1].GetAttributeAsync("value");
        await supplierDropdown.SelectOptionAsync(firstSupplierValue!);

        // Add a comment
        await reviewPage.ItemCommentField(id).FillAsync("Approved with selected supplier");

        // Submit decision
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(id);

        // Verify success and status update
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(id)).ToContainTextAsync("Aprobado");
    }

    [Test]
    public async Task ReviewItem_RejectWithComment()
    {
        var (appId, _) = await SetupSubmittedApplicationAsync();

        var reviewerEmail = $"rid_rej_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Reject", "Reviewer", $"RJLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Select Reject
        await reviewPage.ItemDecisionRadio(itemId, "Reject").CheckAsync();
        await reviewPage.ItemCommentField(itemId).FillAsync("Item rejected - budget constraints");
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);

        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Rechazado");
    }

    [Test]
    public async Task ReviewItem_RequestMoreInfoWithoutComment()
    {
        var (appId, _) = await SetupSubmittedApplicationAsync();

        var reviewerEmail = $"rid_info_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "MoreInfo", "Reviewer", $"MILID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Select Request More Info without comment
        await reviewPage.ItemDecisionRadio(itemId, "RequestMoreInfo").CheckAsync();
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);

        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();
        await Expect(reviewPage.ItemReviewStatusBadge(itemId)).ToContainTextAsync("Requiere información");
    }

    private string _uniqueId = string.Empty;

    private async Task<(int AppId, int ItemId)> SetupSubmittedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"rid_applicant_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "ItemDecision", "Applicant", $"LID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Decision Test Item", 0, "Test specs for decisions", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"S1-{_uniqueId}", "Supplier Alpha", 500m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"S2-{_uniqueId}", "Supplier Beta", 750m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

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

        return (appId, 0);
    }
}
