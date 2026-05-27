using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class ApplicantResponseTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;
    private string _applicantEmail = string.Empty;
    private string _applicantPassword = "Test123!";

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
    public async Task Applicant_Can_Respond_Per_Item_And_Accepted_Items_Advance()
    {
        var (appId, itemId) = await SetupResolvedApplicationAsync(rejectItem: false);

        await LoginAsync(Page, _applicantEmail, _applicantPassword);

        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);

        await Expect(responsePage.Header).ToContainTextAsync("Respuesta del solicitante");

        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();

        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();
        await Expect(responsePage.ApplicationState).ToContainTextAsync("Respuesta finalizada");

        // Reload and confirm response is read-only.
        // The decision-display renders es-CR copy (ItemResponseDecisionCopy.Label),
        // not the raw enum name — the prior "Accept" leak is fixed.
        await responsePage.GotoAsync(BaseUrl, appId);
        await Expect(responsePage.DecisionDisplay(itemId)).ToContainTextAsync("Aceptada");
    }

    [TestCase("Uphold", "ResponseFinalized", "/ApplicantResponse/Appeal/")]
    [TestCase("GrantReopenToDraft", "Draft", "/Application/Details/")]
    [TestCase("GrantReopenToReview", "UnderReview", "/Review")]
    public async Task Reviewer_Can_Resolve_Appeal_With_All_Three_Outcomes(string resolution, string expectedState, string expectedUrlFragment)
    {
        var (appId, itemId, reviewerEmail) = await SetupAppealOpenApplicationAsync();

        await LoginAsync(Page, reviewerEmail, "Test123!");

        var appealPage = new AppealThreadPage(Page);
        await appealPage.GotoAsync(BaseUrl, appId);

        var button = resolution switch
        {
            "Uphold" => appealPage.UpholdButton,
            "GrantReopenToDraft" => appealPage.GrantReopenDraftButton,
            "GrantReopenToReview" => appealPage.GrantReopenReviewButton,
            _ => throw new ArgumentException($"Unknown resolution: {resolution}")
        };

        await button.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(expectedUrlFragment)));
    }

    private async Task<(int AppId, int ItemId, string ReviewerEmail)> SetupAppealOpenApplicationAsync()
    {
        var (appId, itemId) = await SetupResolvedApplicationAsync(rejectItem: true);

        // Applicant: submit rejecting response + open appeal
        await LoginAsync(Page, _applicantEmail, _applicantPassword);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.RejectRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();

        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.OpenAppealButton.ClickAsync();

        var appealPage = new AppealThreadPage(Page);
        await Expect(appealPage.AppealStatus).ToContainTextAsync(UiCopy.State.Open);

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        var reviewerEmail = $"ar_resolver_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Resolver", "Reviewer", $"RSLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        return (appId, itemId, reviewerEmail);
    }

    [Test]
    public async Task Applicant_Can_Open_Appeal_On_Rejected_Items_And_Reviewers_Can_Reply()
    {
        var (appId, itemId) = await SetupResolvedApplicationAsync(rejectItem: true);

        // Applicant logs in and rejects the reviewer's rejection
        await LoginAsync(Page, _applicantEmail, _applicantPassword);

        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.RejectRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();

        // Open appeal
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.OpenAppealButton.ClickAsync();

        var appealPage = new AppealThreadPage(Page);
        await Expect(appealPage.AppealStatus).ToContainTextAsync(UiCopy.State.Open);

        // Applicant posts a message
        await appealPage.PostMessageAsync("Please reconsider — the item is still needed.");
        await Expect(appealPage.SuccessMessage).ToBeVisibleAsync();
        await Expect(appealPage.ApplicantMessages).ToHaveCountAsync(1);

        // Logout applicant
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Login as reviewer and reply
        var reviewerEmail = $"ar_replier_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Reply", "Reviewer", $"RPLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        await appealPage.GotoAsync(BaseUrl, appId);
        await appealPage.PostMessageAsync("We considered all options. Here's more context.");
        await Expect(appealPage.SuccessMessage).ToBeVisibleAsync();
        await Expect(appealPage.Messages).ToHaveCountAsync(2);
        await Expect(appealPage.ReviewerMessages).ToHaveCountAsync(1);
    }

    internal async Task<(int AppId, int ItemId)> SetupResolvedApplicationAsync(bool rejectItem)
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _applicantEmail = $"ar_applicant_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, _applicantEmail, _applicantPassword, "Applicant", "Response", $"ARLID-{_uniqueId}");
        await LoginAsync(Page, _applicantEmail, _applicantPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Response Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"AR1-{_uniqueId}", "Supplier Alpha", 900m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"AR2-{_uniqueId}", "Supplier Beta", 1100m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Reviewer approves/rejects and finalizes
        var reviewerEmail = $"ar_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "Review", "Finalizer", $"RVLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        if (rejectItem)
        {
            await reviewPage.ItemDecisionRadio(itemId, "Reject").CheckAsync();
            await reviewPage.ItemCommentField(itemId).FillAsync("Item rejected for testing");
        }
        else
        {
            await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
            var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
            var suppOptions = await supplierDropdown.Locator("option").AllAsync();
            await supplierDropdown.SelectOptionAsync(await suppOptions[1].GetAttributeAsync("value") ?? "");
        }
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        await reviewPage.FinalizeButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await Expect(Page.Locator($".alert-success:has-text('{UiCopy.ReviewFinalized}')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return (appId, itemId);
    }
}
