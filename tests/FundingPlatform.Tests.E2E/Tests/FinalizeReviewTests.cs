using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class FinalizeReviewTests : AuthenticatedTestBase
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
    public async Task FinalizeWithAllItemsResolved_TransitionsToResolved()
    {
        var appId = await SetupSubmittedApplicationAsync();

        var reviewerEmail = $"fr_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Finalize", "Reviewer", $"FRLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Approve the item with a supplier
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var options = await supplierDropdown.Locator("option").AllAsync();
        await supplierDropdown.SelectOptionAsync(await options[1].GetAttributeAsync("value") ?? "");
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        // Finalize the review
        await reviewPage.FinalizeButton.ClickAsync();

        // Should redirect to queue with success
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await Expect(Page.Locator($".alert-success:has-text('{UiCopy.ReviewFinalized}')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FinalizeWithUnresolvedItems_ShowsWarning_ConfirmForceFinalization()
    {
        var appId = await SetupSubmittedApplicationAsync();

        var reviewerEmail = $"fr_warn_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "WarnFin", "Reviewer", $"WFLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        // Try to finalize without resolving items
        await reviewPage.FinalizeButton.ClickAsync();

        // Should show warning with unresolved items
        await Expect(reviewPage.UnresolvedWarning).ToBeVisibleAsync();
        await Expect(reviewPage.UnresolvedWarning).ToContainTextAsync("Finalization Test Item");

        // Confirm force finalization
        await reviewPage.ForceFinalizationConfirm.ClickAsync();

        // Should redirect to queue with success
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await Expect(Page.Locator($".alert-success:has-text('{UiCopy.ReviewFinalized}')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FinalizeWithUnresolvedItems_CancelKeepsUnderReview()
    {
        var appId = await SetupSubmittedApplicationAsync();

        var reviewerEmail = $"fr_cancel_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "CancelFin", "Reviewer", $"CFLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        // Try to finalize without resolving items
        await reviewPage.FinalizeButton.ClickAsync();

        // Should show warning
        await Expect(reviewPage.UnresolvedWarning).ToBeVisibleAsync();

        // Click Cancel
        await Page.Locator(".unresolved-warning a:has-text('Cancelar')").ClickAsync();

        // Should stay on review page with Under Review state
        await Expect(reviewPage.ApplicationState).ToContainTextAsync("En revisión");
    }

    private async Task<int> SetupSubmittedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"fr_applicant_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "Finalize", "Applicant", $"LID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Finalization Test Item", 0, "Test specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"FR1-{_uniqueId}", "Supplier Alpha", 900m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"FR2-{_uniqueId}", "Supplier Beta", 1100m, "2027-12-31", _testFilePath);
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

        return appId;
    }
}
