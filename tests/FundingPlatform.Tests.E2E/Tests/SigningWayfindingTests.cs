using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// End-to-end tests for spec 007 Signing Stage Wayfinding (SC-007).
/// Each test covers one of the three wayfinding journeys documented in quickstart.md:
///   (a) Reviewer reaches Signing Inbox via tabs.
///   (b) Applicant sees banner + signing panel on response page.
///   (c) /Application/Details/{id} embed continues to render (regression guard).
/// </summary>
[Category("SigningWayfinding")]
public class SigningWayfindingTests : AuthenticatedTestBase
{
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
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"sw-quote-{Guid.NewGuid():N}.pdf");
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
    public async Task ReviewerReachesSigningInboxInTwoClicks()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var reviewerEmail = $"sw_tabs_{uniqueId}@example.com";

        await RegisterUserAsync(Page, reviewerEmail, DefaultPassword, "Wayfinding", "Reviewer", $"SWR-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, DefaultPassword);

        await Page.GotoAsync($"{BaseUrl}/");

        // Click 1: the main-navigation review link (sidebar entry, slug-based for stability).
        await Page.Locator("[data-testid=\"sidebar-entry-review-queue\"]").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review/?(\?|$)"));

        var reviewQueue = new ReviewQueuePage(Page);
        await Expect(reviewQueue.ReviewTabs).ToBeVisibleAsync();
        await Expect(reviewQueue.InitialQueueTab).ToBeVisibleAsync();
        await Expect(reviewQueue.SigningInboxTab).ToBeVisibleAsync();
        Assert.That(await reviewQueue.IsInitialQueueTabActive(), Is.True,
            "Initial Review Queue tab must be active on /Review");
        Assert.That(await reviewQueue.IsSigningInboxTabActive(), Is.False);

        // Click 2: the Signing Inbox sub-tab.
        await reviewQueue.ClickSigningInboxTab();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review/SigningInbox"));
        Assert.That(await reviewQueue.IsSigningInboxTabActive(), Is.True,
            "Signing Inbox tab must be active on /Review/SigningInbox");
        Assert.That(await reviewQueue.IsInitialQueueTabActive(), Is.False);

        // Round-trip: clicking Initial Review Queue returns to /Review.
        await reviewQueue.InitialQueueTab.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review/?(\?|$)"));
        Assert.That(await reviewQueue.IsInitialQueueTabActive(), Is.True);
    }

    [Test]
    public async Task ApplicantSeesBannerAndPanelOnResponsePage()
    {
        var appId = await SeedResponseFinalizedViaUiAsync();

        // Stage A — ResponseFinalized + generated agreement: banner + panel + download.
        var seededBlobKey = await FundingAgreementSeeder.SeedGeneratedAgreementAsync(
            ConnectionString, appId, _adminEmail, CreateBlobServiceClient());
        _seededFiles.Add(seededBlobKey);

        await LoginAsync(Page, _applicantEmail, DefaultPassword);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);

        await Expect(responsePage.ReadyToSignBanner).ToBeVisibleAsync();
        Assert.That(await responsePage.ReadyToSignBanner.TextContentAsync(),
            Does.Contain("listo para firmar"));
        Assert.That(await responsePage.AgreementExecutedBanner.CountAsync(), Is.EqualTo(0),
            "Executed banner must not render at ResponseFinalized");

        var panel = new FundingAgreementPanelPage(Page);
        Assert.That(await panel.IsPanelVisibleAsync(), Is.True,
            "Embedded signing panel must load via async fetch on the response page");
        Assert.That(await panel.HasDownloadLinkAsync(), Is.True,
            "Applicant must see the Download agreement action in the embedded panel");

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Stage B — AgreementExecuted: banner copy flips, signed-download link appears.
        var signedBlobKey = await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, _adminEmail, _applicantEmail, _reviewerEmail, CreateBlobServiceClient());
        _seededFiles.Add(signedBlobKey);

        await LoginAsync(Page, _applicantEmail, DefaultPassword);
        await responsePage.GotoAsync(BaseUrl, appId);

        await Expect(responsePage.AgreementExecutedBanner).ToBeVisibleAsync();
        Assert.That(await responsePage.AgreementExecutedBanner.TextContentAsync(),
            Does.Contain("ha sido ejecutado"));
        Assert.That(await responsePage.ReadyToSignBanner.CountAsync(), Is.EqualTo(0),
            "Ready-to-sign banner must not render at AgreementExecuted");

        var executedPanel = new SigningStagePanelPage(Page);
        await Expect(executedPanel.SignedDownloadLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task ApplicationDetailsEmbedStillRenders()
    {
        var appId = await SeedResponseFinalizedViaUiAsync();
        var seededBlobKey = await FundingAgreementSeeder.SeedGeneratedAgreementAsync(
            ConnectionString, appId, _adminEmail, CreateBlobServiceClient());
        _seededFiles.Add(seededBlobKey);

        // /Application/Details/{id} is Applicant-only (ApplicationController class-level
        // authorize). Regression guard: the embedded signing panel still renders there
        // after the US2 DTO/view-model plumbing changes.
        await LoginAsync(Page, _applicantEmail, DefaultPassword);
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");

        var panel = new FundingAgreementPanelPage(Page);
        Assert.That(await panel.IsPanelVisibleAsync(), Is.True,
            "Signing panel must render on /Application/Details/{id} (FR-005 regression guard)");
        Assert.That(await panel.HasDownloadLinkAsync(), Is.True,
            "Download agreement action must remain available on the Application/Details embed");
    }

    /// <summary>
    /// Drives the UI through the full happy path up to ResponseFinalized (item approved,
    /// review finalized, applicant accepted). Populates <see cref="_applicantEmail"/>,
    /// <see cref="_reviewerEmail"/>, <see cref="_adminEmail"/> as side effects. The
    /// Funding Agreement is NOT generated here — callers seed it via
    /// <see cref="FundingAgreementSeeder"/> to bypass Syncfusion PDF rendering.
    /// </summary>
    private async Task<int> SeedResponseFinalizedViaUiAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _applicantEmail = $"sw_applicant_{_uniqueId}@example.com";
        _reviewerEmail = $"sw_reviewer_{_uniqueId}@example.com";
        _adminEmail = $"sw_admin_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, _adminEmail, DefaultPassword, "Admin", "Wayfinding", $"SWA-{_uniqueId}");
        await AssignRoleAsync(_adminEmail, "Admin");

        await RegisterUserAsync(Page, _applicantEmail, DefaultPassword, "Wayfinding", "Applicant", $"SWP-{_uniqueId}");
        await LoginAsync(Page, _applicantEmail, DefaultPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "SW Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SW1-{_uniqueId}", "Supplier Alpha", 900m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SW2-{_uniqueId}", "Supplier Beta", 1100m, "2027-12-31", _quotationFilePath);
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

        await RegisterUserAsync(Page, _reviewerEmail, DefaultPassword, "Reviewer", "Wayfinding", $"SWR-{_uniqueId}");
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
        await reviewPage.ItemSubmitButton(itemId).ClickAsync();
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
