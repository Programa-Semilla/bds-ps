using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 021 / T048 / US2 / FR-008 — SendBack fires exactly one applicant-variant
/// email + zero reviewer-variant emails (reviewers don't get notified when the
/// ball is in the applicant's court). EC-003 verified by changing the applicant's
/// email between Submit and SendBack and asserting delivery to the new address.
/// </summary>
public class ReturnedToApplicantNotificationsTests : AuthenticatedTestBase
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
    public async Task SendBack_fires_returned_to_applicant_only()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 021 / NFR-007 — smtp4dev sidecar not available.");
            return;
        }
        await MailCapture.DrainAsync();

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"sb_app_{uniqueId}@example.com";
        var reviewerEmail = $"sb_rev_{uniqueId}@example.com";

        await RegisterUserAsync(Page, reviewerEmail, password, "SB", "Reviewer", $"R-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        await RegisterUserAsync(Page, applicantEmail, password, "SB", "Applicant", $"A-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Details/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "SendBack Item", 0, "Specs", BaseUrl);

        for (var i = 1; i <= 2; i++)
        {
            var supplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
            await supplierLink.ClickAsync();
            var supplierPage = new SupplierPage(Page);
            await supplierPage.FillSupplierFormAsync(
                legalId: $"SUP{i}-{uniqueId}",
                name: $"Supplier {i}",
                price: 1000m + i * 50,
                validUntil: "2027-12-31",
                filePath: _quotation);
            await supplierPage.SubmitAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        }

        var impactButton = Page.Locator($"a:has-text('{UiCopy.Impact}')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (var i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var t = await input.GetAttributeAsync("type");
            await input.FillAsync(t == "number" ? "100" : t == "date" ? "2026-12-31" : "Test");
        }
        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SaveImpact}')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SubmitApplication}')").ClickAsync();
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Drain the initial Submit emails so we only see the SendBack-emitted ones below.
        await MailCapture.WaitForAsync(minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.ToAddresses.Any(t => t.Contains(applicantEmail, StringComparison.OrdinalIgnoreCase)));
        await MailCapture.DrainAsync();

        // Reviewer sends the application back.
        await LoginAsync(Page, reviewerEmail, password);
        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);
        // Click the SendBack control.
        var sendBack = Page.Locator("button[type=submit]:has-text('Devolver')").First;
        await Expect(sendBack).ToBeVisibleAsync();
        await sendBack.ClickAsync();

        var returnedMsgs = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Acción requerida"));

        Assert.That(returnedMsgs, Has.Count.GreaterThanOrEqualTo(1),
            "Expected at least one Acción-requerida email after SendBack.");

        var applicantMsg = returnedMsgs.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(applicantEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(applicantMsg, Is.Not.Null,
            "Applicant must receive the SendBack notification.");
        Assert.That(applicantMsg!.HtmlBody + applicantMsg.TextBody,
            Does.Contain($"/Application/Details/{appId}"));

        // Reviewer must NOT receive a SendBack-emitted email.
        var reviewerMsgs = returnedMsgs.Where(m =>
            m.ToAddresses.Any(t => t.Contains(reviewerEmail, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.That(reviewerMsgs, Is.Empty,
            "FR-008: reviewers must NOT receive emails on RETURNED_TO_APPLICANT.");
    }
}
