using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;
using NUnit.Framework;
using static Microsoft.Playwright.Assertions;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// End-to-end tests for spec 007 Amendment 1, SC-010 (Generate Agreement Queue tab).
/// Tests run against an ephemeral SQL container — see <c>AspireFixture</c> for the
/// <c>--EphemeralStorage=true</c> flag that disables the AppHost data volume during tests.
/// </summary>
[TestFixture]
[Category("GenerateAgreementQueue")]
public class GenerateAgreementQueueTests : AuthenticatedTestBase
{
    private string _quotationFilePath = string.Empty;
    private readonly List<string> _seededFiles = [];
    private const string Password = "Test123!";

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"ga-quote-{Guid.NewGuid():N}.pdf");
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

    /// <summary>
    /// SC-010-A: A reviewer with no matching apps sees the empty-state message.
    /// The Aspire fixture is shared across test classes, so sibling classes
    /// (ApplicantResponseTests, FinalizeReviewTests) may have left ResponseFinalized
    /// apps with no agreement — neutralize them via SQL before asserting the
    /// empty-state element so this test is order-independent.
    /// </summary>
    [Test, Order(1)]
    public async Task GenerateAgreementTab_Empty_ShowsEmptyState()
    {
        await FundingAgreementSeeder.ClearGenerateAgreementQueueAsync(ConnectionString, CreateBlobServiceClient());

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var reviewerEmail = $"ga_empty_{uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, Password, "GA", "EmptyReviewer", $"GAE-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, Password);

        var queuePage = new ReviewQueuePage(Page);
        await queuePage.GotoGenerateAgreementAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(queuePage.GenerateAgreementEmpty).ToBeVisibleAsync();
        await Expect(queuePage.GenerateAgreementEmpty)
            .ToContainTextAsync("No hay solicitudes pendientes de convenio");
        Assert.That(await queuePage.IsGenerateAgreementTabActive(), Is.True,
            "Generate Agreement tab should be marked active on its own route.");
    }

    /// <summary>
    /// SC-010-B: A ResponseFinalized application without an agreement appears
    /// in the queue for both Reviewer and Admin roles.
    /// </summary>
    [Test]
    public async Task ReviewerAndAdmin_BothSee_ResponseFinalizedApplicationWithoutAgreement()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (applicationId, _, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);

        // Reviewer #2 (separate from the seeding reviewer) sees the app in the queue
        var reviewerEmail = $"ga_rev_{uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, Password, "GA", "Reviewer", $"GAR-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, Password);

        var queuePage = new ReviewQueuePage(Page);
        await queuePage.GotoAsync(BaseUrl);
        await queuePage.ClickGenerateAgreementTab();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(queuePage.GenerateAgreementTable).ToBeVisibleAsync();
        await Expect(Page.Locator(
            $"[data-testid=generate-agreement-row][data-application-id='{applicationId}']")).ToBeVisibleAsync();

        // Logout and re-login as admin
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        var adminEmail = $"ga_admin_{uniqueId}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "GA", "Admin", $"GAA-{uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        await queuePage.GotoAsync(BaseUrl);
        await queuePage.ClickGenerateAgreementTab();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(queuePage.GenerateAgreementTable).ToBeVisibleAsync();
        await Expect(Page.Locator(
            $"[data-testid=generate-agreement-row][data-application-id='{applicationId}']")).ToBeVisibleAsync();
    }

    /// <summary>
    /// SC-010-C: Full chain — reviewer navigates from the queue to the app's
    /// agreement page; after an agreement is generated the app leaves the queue;
    /// the applicant's response page shows the "ready to sign" banner.
    ///
    /// Note: PDF generation via Syncfusion is not available in the test environment
    /// (same constraint as <see cref="SigningWayfindingTests"/>), so the agreement
    /// is seeded directly via <see cref="FundingAgreementSeeder"/> after the
    /// reviewer has navigated to the agreement page.  The queue-departure and
    /// banner assertions are unaffected by this bypass.
    /// </summary>
    [Test]
    public async Task EndToEndChain_ReviewerGeneratesAgreement_ApplicantSeesReadyToSignBanner()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (applicationId, applicantEmail, applicantPassword) =
            await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);

        var reviewerEmail = $"ga_chain_{uniqueId}@example.com";
        var adminEmail = $"ga_chain_admin_{uniqueId}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "GA", "ChainAdmin", $"GACA-{uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");
        await RegisterUserAsync(Page, reviewerEmail, Password, "GA", "ChainReviewer", $"GAC-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, Password);

        var queuePage = new ReviewQueuePage(Page);
        await queuePage.GotoAsync(BaseUrl);
        await queuePage.ClickGenerateAgreementTab();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the app appears in the queue, then click Open
        await Expect(Page.Locator(
            $"[data-testid=generate-agreement-row][data-application-id='{applicationId}']")).ToBeVisibleAsync();
        await Page.Locator(
            $"[data-testid=generate-agreement-row][data-application-id='{applicationId}'] a:has-text('{UiCopy.Open}')")
            .ClickAsync();

        Assert.That(Regex.IsMatch(Page.Url, $@"/Applications/{applicationId}/FundingAgreement"), Is.True,
            $"Expected to land on funding-agreement details page; was at {Page.Url}");

        // Seed the agreement via SQL (bypasses Syncfusion — same approach as SigningWayfindingTests).
        // This simulates what "Generate agreement" would do in a licensed environment.
        var seededBlobKey = await FundingAgreementSeeder.SeedGeneratedAgreementAsync(
            ConnectionString, applicationId, adminEmail, CreateBlobServiceClient());
        _seededFiles.Add(seededBlobKey);

        // Reload the page and confirm the Download link is now present (agreement seeded)
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator("[data-testid=funding-agreement-download]")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The application should now leave the Generate Agreement tab
        // (queue filter: State == ResponseFinalized AND FundingAgreement IS NULL)
        await queuePage.GotoAsync(BaseUrl);
        await queuePage.ClickGenerateAgreementTab();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator(
            $"[data-testid=generate-agreement-row][data-application-id='{applicationId}']")).ToHaveCountAsync(0);

        // Log back in as the applicant and confirm the ready-to-sign banner
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, applicantEmail, applicantPassword);
        await Page.GotoAsync($"{BaseUrl}/ApplicantResponse/Index/{applicationId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator("[data-testid=signing-banner-ready]")).ToBeVisibleAsync();
    }
}
