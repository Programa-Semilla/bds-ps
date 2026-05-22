using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

[Category("FundingAgreement")]
public class FundingAgreementTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;
    private string _applicantEmail = string.Empty;
    private string _applicantPassword = "Test123!";
    private string _reviewerEmail = string.Empty;
    private string _adminEmail = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"fa-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    [Test]
    public async Task US1_Admin_Generates_Funding_Agreement_And_Can_Download()
    {
        var (appId, _) = await SetupAcceptedApplicationAsync();

        await LoginAsync(Page, _adminEmail, "Test123!");

        var panelPage = new FundingAgreementPanelPage(Page);
        await panelPage.GotoDetailsAsync(BaseUrl, appId);

        Assert.That(await panelPage.IsPanelVisibleAsync(), Is.True, "Panel must be visible to admin.");
        Assert.That(await panelPage.GenerateButton.CountAsync(), Is.GreaterThan(0),
            "Generate button must be rendered for admin when preconditions hold.");

        await panelPage.ClickGenerateAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));
        Assert.That(await panelPage.HasDownloadLinkAsync(), Is.True,
            "Download link must appear after successful generation.");

        var downloadFlow = new FundingAgreementDownloadFlow(Page);
        var bytes = await downloadFlow.CaptureDownloadBytesAsync(panelPage.DownloadLink);

        Assert.That(FundingAgreementDownloadFlow.LooksLikePdf(bytes), Is.True,
            "Downloaded bytes must have a %PDF- header.");
    }

    [Test]
    public async Task US2_Owning_Applicant_Sees_Download_And_No_Generate_Buttons()
    {
        var (appId, _) = await SetupAcceptedApplicationAsync();

        // Admin generates first
        await LoginAsync(Page, _adminEmail, "Test123!");
        var adminPanel = new FundingAgreementPanelPage(Page);
        await adminPanel.GotoDetailsAsync(BaseUrl, appId);
        await adminPanel.ClickGenerateAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Applicant visits their application detail — panel should show Download but no Generate/Regenerate
        await LoginAsync(Page, _applicantEmail, _applicantPassword);
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");

        var applicantPanel = new FundingAgreementPanelPage(Page);
        Assert.That(await applicantPanel.IsPanelVisibleAsync(), Is.True);
        Assert.That(await applicantPanel.HasDownloadLinkAsync(), Is.True);
        Assert.That(await applicantPanel.GenerateButton.CountAsync(), Is.EqualTo(0));
        Assert.That(await applicantPanel.RegenerateButton.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task US3_Admin_Or_Reviewer_Can_Regenerate_Overwriting_Prior_File()
    {
        var (appId, _) = await SetupAcceptedApplicationAsync();

        // Admin generates first
        await LoginAsync(Page, _adminEmail, "Test123!");
        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);
        await panel.ClickGenerateAsync();

        Assert.That(await panel.HasDownloadLinkAsync(), Is.True);

        // Spec 024 — regenerate now opens the shared confirm modal; click confirm.
        await panel.RegenerateButton.ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/\d+/FundingAgreement"));
        Assert.That(await panel.HasDownloadLinkAsync(), Is.True);
    }

    [Test]
    public async Task US4_Generate_Action_Disabled_When_Review_Open()
    {
        // Set up an application in Submitted/UnderReview state (no response, no accepted items)
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _applicantEmail = $"fa_blocked_{_uniqueId}@example.com";
        _adminEmail = $"fa_admin_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, _adminEmail, "Test123!", "Admin", "Funding", $"FALID-{_uniqueId}");
        await AssignRoleAsync(_adminEmail, "Admin");

        await RegisterUserAsync(Page, _applicantEmail, _applicantPassword, "Blocked", "Applicant", $"ALID-{_uniqueId}");
        await LoginAsync(Page, _applicantEmail, _applicantPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Admin visits panel — should be disabled with reason
        await LoginAsync(Page, _adminEmail, "Test123!");
        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);

        Assert.That(await panel.IsPanelVisibleAsync(), Is.True);
        Assert.That(await panel.GenerateButton.CountAsync(), Is.EqualTo(0));
        var reason = await panel.GetDisabledReasonAsync();
        Assert.That(reason, Is.Not.Null);
    }

    [Test]
    public async Task US6_Unauthorized_User_Gets_NonDisclosing_404()
    {
        var (appId, _) = await SetupAcceptedApplicationAsync();

        var strangerEmail = $"fa_stranger_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, strangerEmail, "Test123!", "Stranger", "Applicant", $"STLID-{_uniqueId}");
        await LoginAsync(Page, strangerEmail, "Test123!");

        var response = await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement/Download");
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(404));
    }

    private async Task<(int AppId, int ItemId)> SetupAcceptedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _applicantEmail = $"fa_applicant_{_uniqueId}@example.com";
        _reviewerEmail = $"fa_reviewer_{_uniqueId}@example.com";
        _adminEmail = $"fa_admin_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, _adminEmail, "Test123!", "Admin", "Funding", $"FALID-{_uniqueId}");
        await AssignRoleAsync(_adminEmail, "Admin");

        await RegisterUserAsync(Page, _applicantEmail, _applicantPassword, "Funding", "Applicant", $"APLID-{_uniqueId}");
        await LoginAsync(Page, _applicantEmail, _applicantPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "FA Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"FA1-{_uniqueId}", "Supplier Alpha", 900m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"FA2-{_uniqueId}", "Supplier Beta", 1100m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);

        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await RegisterUserAsync(Page, _reviewerEmail, "Test123!", "Reviewer", "Funding", $"RVLID-{_uniqueId}");
        await AssignRoleAsync(_reviewerEmail, "Reviewer");
        await LoginAsync(Page, _reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var suppOptions = await supplierDropdown.Locator("option").AllAsync();
        await supplierDropdown.SelectOptionAsync(await suppOptions[1].GetAttributeAsync("value") ?? "");
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        await reviewPage.FinalizeButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Applicant accepts
        await LoginAsync(Page, _applicantEmail, _applicantPassword);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return (appId, itemId);
    }
}
