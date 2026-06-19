using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 021 / T043 / US1 / FR-007 / SC-001 — first-time submit fires two
/// outbox rows that fan out to the applicant + each reviewer who shares a
/// group with the applicant. Verifies subject lines, CTA hrefs, sender
/// display, no inline &lt;img&gt;, no Capital Semilla/Forge leakage.
/// </summary>
public class ApplicationSubmittedNotificationsTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"q-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Test quotation document content");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
        if (MailCapture is not null) await MailCapture.DrainAsync();
    }

    [Test]
    public async Task SubmitFiresApplicantAndReviewerVariants()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive(
                "Spec 021 / NFR-007 — smtp4dev sidecar did not start in the fixture; cannot run mail-capture assertions.");
            return;
        }
        await MailCapture.DrainAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        // Spec 021 / FR-017 — emails must be in the dev-default allowlist
        // (@programa-semilla.test) so the allowlist filter doesn't block them.
        var applicantEmail = $"submit_app_{uniqueId}@programa-semilla.test";
        var reviewerEmail = $"submit_rev_{uniqueId}@programa-semilla.test";

        // 1) Register a reviewer (auto-assigned to every seeded group → shares groups with the applicant below).
        await RegisterUserAsync(Page, reviewerEmail, password, "Test", "Reviewer", $"R-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        // 2) Register an applicant and submit a complete application.
        await RegisterUserAsync(Page, applicantEmail, password, "Test", "Applicant", $"A-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Notification Test Item", 0, "Specs for notification test", BaseUrl);

        // Two suppliers (MinQuotationsPerItem default 2).
        for (var i = 1; i <= 2; i++)
        {
            var supplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
            await Expect(supplierLink).ToBeVisibleAsync();
            await supplierLink.ClickAsync();
            var supplierPage = new SupplierPage(Page);
            await supplierPage.FillSupplierFormAsync(
                legalId: $"SUP{i}-{uniqueId}",
                name: $"Supplier {i}",
                price: 1000m + i * 50,
                validUntil: "2027-12-31",
                filePath: _testFilePath);
            await supplierPage.SubmitAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        }

        // 3) Impact + submit through the draft editor → /review.
        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();

        // 4) Wait for the worker to pick up the two outbox rows and dispatch.
        //    Expect at least 2 messages (1 applicant + 1 reviewer). Other reviewers
        //    may exist from earlier tests in the shared fixture; we count by recipient.
        var allMessages = await MailCapture.WaitForAsync(
            minCount: 2,
            timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.Contains("Solicitud #" + appId) || m.Subject.Contains("Nueva solicitud"));

        // Applicant message: confirmation subject + deep link to /Application/Details/{id}.
        // The resolver explicitly excludes the applicant from the reviewer bucket
        // (NotificationRecipientResolver — FR-007 / §Recipient Rules), so the
        // applicant only ever appears on the APPLICATION_SUBMITTED_APPLICANT row.
        // Filter by recipient + subject anyway to keep the assertion deterministic
        // when other reviewers from prior tests are present in the shared fixture.
        var applicantMsg = allMessages.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(applicantEmail, StringComparison.OrdinalIgnoreCase)) &&
            m.Subject.Contains("Recibimos tu solicitud"));
        Assert.That(applicantMsg, Is.Not.Null,
            $"Expected at least one applicant-variant email to {applicantEmail}.");
        Assert.That(applicantMsg!.Subject, Does.Contain("Recibimos tu solicitud"));
        Assert.That(applicantMsg.Subject, Does.Contain($"Solicitud #{appId}"));
        Assert.That(applicantMsg.HtmlBody + applicantMsg.TextBody,
            Does.Contain($"/Application/Details/{appId}"));

        // Reviewer message: review subject + deep link to /Review/{id}.
        var reviewerMsg = allMessages.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(reviewerEmail, StringComparison.OrdinalIgnoreCase)) &&
            m.Subject.StartsWith("Nueva solicitud para revisar"));
        Assert.That(reviewerMsg, Is.Not.Null,
            $"Expected at least one reviewer-variant email to {reviewerEmail}.");
        Assert.That(reviewerMsg!.Subject, Does.StartWith("Nueva solicitud para revisar"));
        Assert.That(reviewerMsg.HtmlBody + reviewerMsg.TextBody, Does.Contain($"/Review/{appId}"));

        // Spec 041 / T013 brand-shell sweep: hosted logo + partner strip + teal CTA
        // + ALIA naming + support phone, on both representative redesigned emails.
        foreach (var msg in new[] { applicantMsg, reviewerMsg })
        {
            Assert.That(msg.FromDisplayName, Does.Contain("Programa Semilla"),
                "Sender display must read 'Programa Semilla / Sistema de Banca para el Desarrollo'.");
            Assert.That(msg.HtmlBody, Does.Contain("<img"),
                "Spec 041 / FR-002: branded email must carry the hosted logo + partner strip.");
            Assert.That(msg.HtmlBody, Does.Contain("/lib/brand/partners-footer.png"),
                "FR-006: partner-strip footer must be present on every email.");
            Assert.That(msg.HtmlBody, Does.Contain("#008a9e"),
                "FR-003: branded teal palette/CTA must be present.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Contain("+506 4600-1234"),
                "FR-006: support phone must be present on every email.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Contain("ALIA"),
                "FR-007: ALIA platform naming must appear in body copy.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Capital Semilla"),
                "SC-006: 'Capital Semilla' must not appear in any email.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Forge"),
                "SC-006: 'Forge' must not appear in any email.");
        }
    }
}
