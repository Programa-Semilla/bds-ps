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
        var applicantEmail = $"submit_app_{uniqueId}@example.com";
        var reviewerEmail = $"submit_rev_{uniqueId}@example.com";

        // 1) Register a reviewer (auto-assigned to every seeded group → shares groups with the applicant below).
        await RegisterUserAsync(Page, reviewerEmail, password, "Test", "Reviewer", $"R-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        // 2) Register an applicant and submit a complete application.
        await RegisterUserAsync(Page, applicantEmail, password, "Test", "Applicant", $"A-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
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
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        }

        // Impact (required by Submit validation).
        var impactButton = Page.Locator($"a:has-text('{UiCopy.Impact}')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (var i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SaveImpact}')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // 3) Submit.
        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SubmitApplication}')").ClickAsync();
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();

        // 4) Wait for the worker to pick up the two outbox rows and dispatch.
        //    Expect at least 2 messages (1 applicant + 1 reviewer). Other reviewers
        //    may exist from earlier tests in the shared fixture; we count by recipient.
        var allMessages = await MailCapture.WaitForAsync(
            minCount: 2,
            timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.Contains("Solicitud #" + appId) || m.Subject.Contains("Nueva solicitud"));

        // Applicant message: confirmation subject + deep link to /Application/Details/{id}.
        var applicantMsg = allMessages.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(applicantEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(applicantMsg, Is.Not.Null,
            $"Expected at least one captured email to the applicant {applicantEmail}.");
        Assert.That(applicantMsg!.Subject, Does.Contain("Recibimos tu solicitud"));
        Assert.That(applicantMsg.Subject, Does.Contain($"Solicitud #{appId}"));
        Assert.That(applicantMsg.HtmlBody + applicantMsg.TextBody,
            Does.Contain($"/Application/Details/{appId}"));

        // Reviewer message: review subject + deep link to /Review/{id}.
        var reviewerMsg = allMessages.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(reviewerEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(reviewerMsg, Is.Not.Null,
            $"Expected at least one captured email to the reviewer {reviewerEmail}.");
        Assert.That(reviewerMsg!.Subject, Does.StartWith("Nueva solicitud para revisar"));
        Assert.That(reviewerMsg.HtmlBody + reviewerMsg.TextBody, Does.Contain($"/Review/{appId}"));

        // Sender display + brand-grep gate + no inline <img>.
        foreach (var msg in new[] { applicantMsg, reviewerMsg })
        {
            Assert.That(msg.FromDisplayName, Does.Contain("Programa Semilla"),
                "FR-014 / spec 019 sender display must read 'Programa Semilla / Sistema de Banca para el Desarrollo'.");
            Assert.That(msg.HtmlBody, Does.Not.Contain("<img"),
                "NFR-001: no inline <img> in any email body.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Capital Semilla"),
                "FR-027 / SC-006: 'Capital Semilla' must not appear in any email.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Forge"),
                "FR-027 / SC-006: 'Forge' must not appear in any email.");
        }
    }
}
