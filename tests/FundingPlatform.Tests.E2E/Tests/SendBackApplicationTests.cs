using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class SendBackApplicationTests : AuthenticatedTestBase
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
    public async Task SendBack_TransitionsToDraft_ApplicantSeesComments_CanResubmit()
    {
        var appId = await SetupSubmittedApplicationAsync();

        // Login as reviewer
        var reviewerEmail = $"sb_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "SendBack", "Reviewer", $"SBLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        // Request more info on the item with a comment
        await reviewPage.ItemDecisionRadio(itemId, "RequestMoreInfo").CheckAsync();
        await reviewPage.ItemCommentField(itemId).FillAsync("Please provide updated specifications");
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        // Send back the application (accept the confirm dialog)
        Page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await Page.Locator("button:has-text('Devolver')").ClickAsync();

        // Should redirect to queue with success message
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await Expect(Page.Locator($".alert-success:has-text('{UiCopy.ApplicationSentBack}')")).ToBeVisibleAsync();

        // Logout and login as applicant
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        var applicantEmail = $"sb_applicant_{_uniqueId}@example.com";
        await LoginAsync(Page, applicantEmail, "Test123!");

        // Navigate to the application
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");

        // Verify state is Draft
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Borrador')")).ToBeVisibleAsync();

        // Verify reviewer comments are visible
        await Expect(Page.Locator("text=Please provide updated specifications")).ToBeVisibleAsync();

        // Verify applicant can resubmit — the draft editor surfaces the gated submit.
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var submitButton = Page.Locator("[data-testid=application-edit-submit]");
        await Expect(submitButton).ToBeVisibleAsync();
    }

    private async Task<int> SetupSubmittedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"sb_applicant_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "SendBack", "Applicant", $"LID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Send Back Item", 0, "Original specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SB1-{_uniqueId}", "Supplier One", 1000m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SB2-{_uniqueId}", "Supplier Two", 1200m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return appId;
    }
}
