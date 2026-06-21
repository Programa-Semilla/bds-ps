using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 042 (audit-email brand-lift) — the two spec-040 auditor-workflow emails
/// (<c>SentToAuditAuditor</c> and <c>ReturnedToReviewerFromAudit</c>) were authored on a
/// parallel branch before the spec-041 brand redesign and were not lifted in the merge.
/// This drives the real reviewer→audit→return ceremony and asserts both captured emails
/// now carry the branded design system (hosted logo + partner-strip footer, teal CTA,
/// ALIA naming, support phone, absolute image host) exactly like the other emails — the
/// coverage gap that let the plain-body audit emails ship unnoticed.
/// </summary>
[TestFixture]
[Category("AuditNotificationsBrand")]
public class AuditNotificationsBrandTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"anb-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
        if (MailCapture is not null) await MailCapture.DrainAsync();
    }

    [Test]
    public async Task SendToAudit_AndReturn_BothEmailsAreBranded()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive(
                "Spec 041 / NFR-007 — smtp4dev sidecar did not start in the fixture; cannot run mail-capture assertions.");
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);

        // Recipients must be allowlisted (@programa-semilla.test) AND in the application's
        // stage group — RegisterUserAsync assigns every seeded group, so both qualify for
        // the group-scoped audit recipient buckets.
        var auditorEmail = $"anb_aud_{uniqueId}@programa-semilla.test";
        await RegisterUserAsync(Page, auditorEmail, Password, "Anb", "Auditor", $"ANBA-{uniqueId}");
        await AssignRoleAsync(auditorEmail, "Auditor");

        var reviewerEmail = $"anb_rev_{uniqueId}@programa-semilla.test";
        await RegisterUserAsync(Page, reviewerEmail, Password, "Anb", "Reviewer", $"ANBR-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        // ---- Transition 1: reviewer sends to audit → SentToAuditAuditor (auditor) ----
        await MailCapture.DrainAsync();

        await LoginAsync(Page, reviewerEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        await Expect(Page.Locator("[data-testid=reviewer-checklist-card]")).ToBeVisibleAsync();

        var sendBtn = Page.Locator("[data-testid=reviewer-send-to-audit]");
        var reviewerChecks = Page.Locator("[data-testid=reviewer-check][data-required='true']");
        var reviewerRequired = await reviewerChecks.CountAsync();
        for (var i = 0; i < reviewerRequired; i++)
        {
            await reviewerChecks.Nth(i).CheckAsync();
        }
        await Expect(sendBtn).ToBeEnabledAsync();
        await sendBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var sentToAuditMsgs = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Nueva solicitud en auditoría"));
        var auditorMsg = sentToAuditMsgs.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(auditorEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(auditorMsg, Is.Not.Null,
            $"Expected a 'Nueva solicitud en auditoría' email to the stage-group auditor {auditorEmail}.");
        AssertBrandCompliant(auditorMsg!, "SentToAuditAuditor");

        // ---- Transition 2: auditor returns to reviewer → ReturnedToReviewerFromAudit ----
        await MailCapture.DrainAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, auditorEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");

        var firstItem = Page.Locator("[data-testid=audit-checklist-item]").First;
        await firstItem.Locator("[data-testid=audit-mark-noncompliant]").CheckAsync();
        await firstItem.Locator("[data-testid=audit-mark-reason]").FillAsync("Falta el documento X.");
        await Expect(Page.Locator("[data-testid=audit-return]")).ToBeEnabledAsync();
        await Page.Locator("[data-testid=audit-return]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var returnedMsgs = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.Contains("Solicitud devuelta por auditoría"));
        var returnedReviewerMsg = returnedMsgs.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(reviewerEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(returnedReviewerMsg, Is.Not.Null,
            $"Expected a 'Solicitud devuelta por auditoría' email to the stage-group reviewer {reviewerEmail}.");
        AssertBrandCompliant(returnedReviewerMsg!, "ReturnedToReviewerFromAudit");

        // The findings list must survive into the branded body (the return email's payload).
        Assert.That(returnedReviewerMsg!.HtmlBody + returnedReviewerMsg.TextBody,
            Does.Contain("Falta el documento X."),
            "The audit findings must render inside the branded return email.");
    }

    /// <summary>
    /// Spec 042 — the brand-shell sweep applied to every redesigned email (mirrors
    /// ApplicationSubmittedNotificationsTests): hosted logo + partner strip, absolute image
    /// host (never the stale localhost default), teal CTA, ALIA naming, support phone, and
    /// no legacy "Capital Semilla"/"Forge" leakage.
    /// </summary>
    private static void AssertBrandCompliant(CapturedMessage msg, string label)
    {
        Assert.Multiple(() =>
        {
            Assert.That(msg.FromDisplayName, Does.Contain("Programa Semilla"),
                $"{label}: sender display must read 'Programa Semilla'.");
            Assert.That(msg.HtmlBody, Does.Contain("<img"),
                $"{label}: FR-002 — branded email must carry the hosted logo + partner strip.");
            Assert.That(msg.HtmlBody, Does.Contain("/lib/brand/partners-footer.png"),
                $"{label}: FR-006 — partner-strip footer must be present.");
            Assert.That(msg.HtmlBody, Does.Not.Contain("localhost:5000"),
                $"{label}: image-host bugfix — images must use the real host, not localhost:5000.");
            Assert.That(msg.HtmlBody, Does.Match("https?://[^\"']+/lib/brand/partners-footer\\.png"),
                $"{label}: image-host bugfix — partner-strip image must be an absolute URL.");
            Assert.That(msg.HtmlBody, Does.Contain("#008a9e"),
                $"{label}: FR-003 — branded teal palette/CTA must be present.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Contain("+506 4600-1234"),
                $"{label}: FR-006 — support phone must be present.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Contain("ALIA"),
                $"{label}: FR-007 — ALIA platform naming must appear in body copy.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Capital Semilla"),
                $"{label}: SC-006 — 'Capital Semilla' must not appear.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Forge"),
                $"{label}: SC-006 — 'Forge' must not appear.");
            Assert.That(msg.HtmlBody + msg.TextBody, Does.Not.Contain("Sistema de Banca para el Desarrollo"),
                $"{label}: the stale sign-off must be replaced by 'Equipo Programa Semilla'.");
        });
    }
}
