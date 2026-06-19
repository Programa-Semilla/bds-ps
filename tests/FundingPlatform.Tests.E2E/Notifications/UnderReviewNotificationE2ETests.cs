using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 041 / US2 / T023 / FR-011 — opening a Submitted application for review
/// (the Submitted → UnderReview transition) fires exactly one "Tu solicitud está
/// en revisión" applicant email; re-opening the page does not duplicate it, and
/// the reviewer never receives it.
/// </summary>
public class UnderReviewNotificationE2ETests : AuthenticatedTestBase
{
    private string _quotation = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotation = Path.Combine(Path.GetTempPath(), $"q-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotation, "Test quotation document content");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (File.Exists(_quotation)) File.Delete(_quotation);
        if (MailCapture is not null) await MailCapture.DrainAsync();
    }

    [Test]
    public async Task ReviewerOpen_fires_one_under_review_email_no_duplicate_on_reopen()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 021 / NFR-007 — smtp4dev sidecar not available.");
            return;
        }
        await MailCapture.DrainAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"ur_app_{uniqueId}@programa-semilla.test";
        var reviewerEmail = $"ur_rev_{uniqueId}@programa-semilla.test";

        await RegisterUserAsync(Page, reviewerEmail, password, "UR", "Reviewer", $"R-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        await RegisterUserAsync(Page, applicantEmail, password, "UR", "Applicant", $"A-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "UnderReview Item", 0, "Specs", BaseUrl);

        for (var i = 1; i <= 2; i++)
        {
            var supplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
            await supplierLink.ClickAsync();
            var supplierPage = new SupplierPage(Page);
            await supplierPage.FillSupplierFormAsync(
                legalId: $"SUP{i}-{uniqueId}", name: $"Supplier {i}",
                price: 1000m + i * 50, validUntil: "2027-12-31", filePath: _quotation);
            await supplierPage.SubmitAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        }

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Drain the submit emails so only the under-review email remains below.
        await MailCapture.WaitForAsync(minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.ToAddresses.Any(t => t.Contains(applicantEmail, StringComparison.OrdinalIgnoreCase)));
        await MailCapture.DrainAsync();

        // Reviewer opens the application → Submitted → UnderReview transition.
        await LoginAsync(Page, reviewerEmail, password);
        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var underReview = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Tu solicitud está en revisión"));

        var applicantMsgs = underReview.Where(m =>
            m.ToAddresses.Any(t => t.Contains(applicantEmail, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.That(applicantMsgs, Has.Count.EqualTo(1),
            "FR-011: exactly one under-review email to the applicant.");
        Assert.That(applicantMsgs[0].HtmlBody + applicantMsgs[0].TextBody,
            Does.Contain($"/Application/Details/{appId}"), "CTA deep-links to the application.");
        Assert.That(applicantMsgs[0].HtmlBody, Does.Contain("<img"),
            "Spec 041: branded email carries the hosted logo + partner strip.");

        // Reviewer must NOT receive the applicant-only notice.
        Assert.That(
            underReview.Any(m => m.ToAddresses.Any(t => t.Contains(reviewerEmail, StringComparison.OrdinalIgnoreCase))),
            Is.False, "FR-011: the under-review notice is applicant-only.");

        // Re-open the page → already UnderReview → no second email.
        await MailCapture.DrainAsync();
        await reviewPage.GotoAsync(BaseUrl, appId);
        await Task.Delay(TimeSpan.FromSeconds(8)); // allow any stray enqueue+dispatch cycle to run
        var afterReopen = await MailCapture.ListAsync(applicantEmail);
        Assert.That(
            afterReopen.Count(m => m.Subject.StartsWith("Tu solicitud está en revisión")),
            Is.EqualTo(0), "re-opening the page must not duplicate the under-review email.");
    }
}
