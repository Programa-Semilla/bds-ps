using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 021 / US9 / FR-035–FR-041 / SC-017 / SC-018 — drives the real applicant
/// journey for self-service removal:
///   • Withdrawing an UnderReview application removes it from the applicant
///     dashboard AND emails the stage-group reviewer (CTA → /Review, not
///     /Review/{id}, because the application is soft-deleted).
///   • Deleting a Draft removes it from the dashboard and sends zero email.
/// </summary>
public class ApplicantRemovalNotificationTests : AuthenticatedTestBase
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

    private async Task LogoutAsync()
        => await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

    private async Task ConfirmRemovalAsync(int appId)
    {
        await Page.Locator("[data-testid=application-row-remove]").First.ClickAsync();
        var confirm = Page.Locator($"#remove-confirm-{appId} [data-testid=confirm-button]");
        await Expect(confirm).ToBeVisibleAsync();
        await confirm.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/?$"));
    }

    [Test]
    public async Task WithdrawUnderReview_RemovesFromDashboard_AndEmailsReviewer()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive(
                "Spec 021 / NFR-007 — smtp4dev sidecar did not start in the fixture; cannot run mail-capture assertions.");
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"wd_app_{uniqueId}@programa-semilla.test";
        var reviewerEmail = $"wd_rev_{uniqueId}@programa-semilla.test";

        // Reviewer shares every seeded group with the applicant (RegisterUserAsync
        // auto-assigns all groups), so the withdrawal notification has a recipient.
        await RegisterUserAsync(Page, reviewerEmail, password, "Test", "Reviewer", $"R-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        await RegisterUserAsync(Page, applicantEmail, password, "Test", "Applicant", $"A-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Withdrawal Test Item", 0, "Specs", BaseUrl);
        for (var i = 1; i <= 2; i++)
        {
            await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
            var supplierPage = new SupplierPage(Page);
            await supplierPage.FillSupplierFormAsync($"SUP{i}-{uniqueId}", $"Supplier {i}", 1000m + i * 50, "2027-12-31", _testFilePath);
            await supplierPage.SubmitAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        }
        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);

        // Reviewer opens the application → transitions Submitted → UnderReview.
        await LogoutAsync();
        await LoginAsync(Page, reviewerEmail, password);
        await new ReviewApplicationPage(Page).GotoAsync(BaseUrl, appId);
        await LogoutAsync();

        // Discard the submission emails; we assert only on the withdrawal mail.
        await MailCapture.DrainAsync();

        // Applicant withdraws from the dashboard.
        await LoginAsync(Page, applicantEmail, password);
        await appPage.GotoListAsync(BaseUrl);
        await ConfirmRemovalAsync(appId);

        // Left the applicant dashboard (this was the applicant's only application).
        await Expect(Page.Locator("text=Aún no tiene solicitudes")).ToBeVisibleAsync();

        // Left the reviewer queue.
        await LogoutAsync();
        await LoginAsync(Page, reviewerEmail, password);
        await Page.GotoAsync($"{BaseUrl}/Review");
        await Expect(Page.Locator($"a[href*='/Review/{appId}']")).ToHaveCountAsync(0);

        // Reviewer received the withdrawal email; CTA → /Review queue, not /Review/{id}.
        var messages = await MailCapture.WaitForAsync(
            minCount: 1,
            timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Solicitud retirada")
                         && m.ToAddresses.Any(t => t.Contains(reviewerEmail, StringComparison.OrdinalIgnoreCase)));

        var msg = messages.First(m =>
            m.ToAddresses.Any(t => t.Contains(reviewerEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(msg.Subject, Does.StartWith("Solicitud retirada"));
        Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain($"/Review/{appId}"),
            "FR-040: withdrawn application is soft-deleted; CTA must target the queue, not the dead detail route.");
        Assert.That(msg.HtmlBody, Does.Not.Contain("<img"), "NFR-001: no inline <img>.");
        Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("financiamiento"),
            "FR-029: 'financiamiento' must not appear on applicant-facing surfaces.");
    }

    [Test]
    public async Task DeleteDraft_RemovesFromDashboard_WithNoEmail()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 021 / NFR-007 — smtp4dev sidecar did not start in the fixture.");
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"del_app_{uniqueId}@programa-semilla.test";

        await RegisterUserAsync(Page, applicantEmail, password, "Test", "Applicant", $"A-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        await MailCapture.DrainAsync();

        await appPage.GotoListAsync(BaseUrl);
        await ConfirmRemovalAsync(appId);

        await Expect(Page.Locator("text=Aún no tiene solicitudes")).ToBeVisibleAsync();

        // No notification for a draft deletion. Give the worker a poll cycle, then assert empty.
        await Task.Delay(TimeSpan.FromSeconds(8));
        var messages = await MailCapture.ListAsync();
        Assert.That(messages, Is.Empty, "FR-035: deleting a Draft must not enqueue any notification.");
    }
}
