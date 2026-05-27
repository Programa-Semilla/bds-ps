using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 — US1 (generator shown by name, not GUID) and US2 (confirm before
/// executing/rejecting the signed convenio). Both exercise the funding-agreement
/// page (<c>/Applications/{id}/FundingAgreement</c>) on the real reviewer journey.
/// </summary>
[Category("ReviewFundingUx")]
public class GeneratorNameAndConfirmTests : AuthenticatedTestBase
{
    private static readonly Regex GuidPattern =
        new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    private string _quotationFilePath = string.Empty;
    private string _uniqueId = string.Empty;
    private string _applicantEmail = string.Empty;
    private string _reviewerEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private readonly List<string> _seededFiles = [];
    private const string DefaultPassword = "Test123!";

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"rfx-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
        foreach (var path in _seededFiles)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        _seededFiles.Clear();
    }

    [Test]
    public async Task US1_GeneratorAttribution_ShowsName_NeverGuid()
    {
        var appId = await SeedResponseFinalizedViaUiAsync();

        // Generator is the admin ("FA Admin"), attributed via the seeder.
        var seededBlobKey = await FundingAgreementSeeder.SeedGeneratedAgreementAsync(
            ConnectionString, appId, _adminEmail, CreateBlobServiceClient());
        _seededFiles.Add(seededBlobKey);

        // Admin can always view the funding-agreement page.
        await LoginAsync(Page, _adminEmail, DefaultPassword);
        var panel = new FundingAgreementPanelPage(Page);
        await panel.GotoDetailsAsync(BaseUrl, appId);

        await Expect(panel.Metadata).ToBeVisibleAsync();
        var metadata = await panel.GetGeneratedAtMetadataAsync() ?? string.Empty;

        Assert.That(metadata, Does.Contain("por FA Admin"),
            "Attribution must show the generator's human-readable name.");
        Assert.That(GuidPattern.IsMatch(metadata), Is.False,
            $"Attribution must never contain a raw GUID. Was: {metadata}");
    }

    [Test]
    public async Task US2_Approve_RequiresConfirmation_DismissLeavesPending()
    {
        var appId = await SeedResponseFinalizedViaUiAsync();
        await SeedPendingUploadAsync(appId);

        await LoginAsync(Page, _reviewerEmail, DefaultPassword);
        var panel = new SigningStagePanelPage(Page);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        await Expect(panel.PendingCard).ToBeVisibleAsync();

        // Click Aprobar — the consequence-stated confirm appears, no commit yet.
        await panel.ApproveButton.ClickAsync();
        await Expect(panel.ConfirmModalBody).ToBeVisibleAsync();
        Assert.That(await panel.ConfirmModalBody.TextContentAsync(), Does.Contain("Esto ejecuta el convenio."));

        // Dismiss → no state change: still pending, not executed.
        await panel.ConfirmModalCancelButton.ClickAsync();
        await Expect(panel.PendingCard).ToBeVisibleAsync();
        Assert.That(await panel.IsExecutedBadgeVisible(), Is.False);

        // Confirm → executes.
        await panel.ApprovePending();
        await Expect(Page.Locator("[data-testid=funding-agreement-executed-badge]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task US2_Reject_RequiresConfirmation_AndStillRequiresComment()
    {
        var appId = await SeedResponseFinalizedViaUiAsync();
        await SeedPendingUploadAsync(appId);

        await LoginAsync(Page, _reviewerEmail, DefaultPassword);
        var panel = new SigningStagePanelPage(Page);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        await Expect(panel.PendingCard).ToBeVisibleAsync();

        // Click Rechazar with no comment → confirm appears with the consequence copy.
        await panel.RejectButton.ClickAsync();
        await Expect(panel.ConfirmModalBody).ToBeVisibleAsync();
        Assert.That(await panel.ConfirmModalBody.TextContentAsync(),
            Does.Contain("Esto rechaza la carga"));

        // Confirm with empty comment → the server backstop still blocks the reject:
        // the upload remains pending.
        await panel.ConfirmModalButton.ClickAsync();
        await Expect(panel.PendingCard).ToBeVisibleAsync();
        Assert.That(await panel.IsExecutedBadgeVisible(), Is.False);

        // With a comment, confirm rejects: the pending card clears.
        await panel.RejectPending("Faltan firmas en la página 2.");
        await Expect(panel.PendingCard).ToHaveCountAsync(0);
    }

    private async Task SeedPendingUploadAsync(int appId)
    {
        var key = await FundingAgreementSeeder.SeedPendingSignedUploadAsync(
            ConnectionString, appId, _adminEmail, _applicantEmail, CreateBlobServiceClient());
        _seededFiles.Add(key);
    }

    /// <summary>
    /// Drives the UI to ResponseFinalized (item approved, review finalized,
    /// applicant accepted), exposing the three actor emails. No agreement yet.
    /// </summary>
    private async Task<int> SeedResponseFinalizedViaUiAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _applicantEmail = $"rfx_applicant_{_uniqueId}@example.com";
        _reviewerEmail = $"rfx_reviewer_{_uniqueId}@example.com";
        _adminEmail = $"rfx_admin_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, _adminEmail, DefaultPassword, "FA", "Admin", $"RFA-{_uniqueId}");
        await AssignRoleAsync(_adminEmail, "Admin");

        await RegisterUserAsync(Page, _applicantEmail, DefaultPassword, "FA", "Applicant", $"RFP-{_uniqueId}");
        await LoginAsync(Page, _applicantEmail, DefaultPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "RFX Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"RX1-{_uniqueId}", "Supplier Alpha", 900m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"RX2-{_uniqueId}", "Supplier Beta", 1100m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await RegisterUserAsync(Page, _reviewerEmail, DefaultPassword, "FA", "Reviewer", $"RFR-{_uniqueId}");
        await AssignRoleAsync(_reviewerEmail, "Reviewer");
        await LoginAsync(Page, _reviewerEmail, DefaultPassword);

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

        await LoginAsync(Page, _applicantEmail, DefaultPassword);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return appId;
    }
}
