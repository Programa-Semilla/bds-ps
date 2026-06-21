using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 043 / US4 (T039) — the non-blocking regulatory-freshness warning shows on the
/// reviewer send-to-audit screen and the auditor detail screen (FR-010), and the daily
/// stale-value digest (triggered via the dev seam) is captured for the group-scoped
/// auditor in smtp4dev (research D3 — direct send, no outbox).
/// </summary>
[TestFixture]
[Category("RegulatoryFreshnessDigest")]
public class RegulatoryFreshnessWarningDigestTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"rfd-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
        if (MailCapture is not null) await MailCapture.DrainAsync();
    }

    [Test]
    public async Task Warning_OnReviewerAndAuditorScreens_AndDigestCaptured()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("smtp4dev sidecar did not start; cannot run the digest mail-capture assertion.");
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);

        // Allowlisted (@programa-semilla.test) + all groups (RegisterUserAsync) so the auditor
        // qualifies for the group-scoped digest.
        var auditorEmail = $"rfd_aud_{uniqueId}@programa-semilla.test";
        await RegisterUserAsync(Page, auditorEmail, Password, "Rfd", "Auditor", $"RFDA-{uniqueId}");
        await AssignRoleAsync(auditorEmail, "Auditor");

        var reviewerEmail = $"rfd_rev_{uniqueId}@programa-semilla.test";
        await RegisterUserAsync(Page, reviewerEmail, Password, "Rfd", "Reviewer", $"RFDR-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");

        // ---- Warning on the reviewer send-to-audit screen ----
        await LoginAsync(Page, reviewerEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        var reviewerWarning = Page.Locator("[data-testid=regulatory-freshness-warning]");
        await Expect(reviewerWarning).ToBeVisibleAsync();
        await Expect(reviewerWarning).ToContainTextAsync("sin revisar");
        // FR-010 — the warning must NAME the at-risk provider (not just render).
        await Expect(reviewerWarning).ToContainTextAsync("Supplier ");

        // Reviewer sends to audit so the app enters the audit pipeline (PendingAudit).
        var reviewerChecks = Page.Locator("[data-testid=reviewer-check][data-required='true']");
        var count = await reviewerChecks.CountAsync();
        for (var i = 0; i < count; i++) await reviewerChecks.Nth(i).CheckAsync();
        var sendBtn = Page.Locator("[data-testid=reviewer-send-to-audit]");
        await Expect(sendBtn).ToBeEnabledAsync();
        await sendBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // ---- Warning on the auditor detail screen ----
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, auditorEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        var auditorWarning = Page.Locator("[data-testid=regulatory-freshness-warning]");
        await Expect(auditorWarning).ToBeVisibleAsync();
        await Expect(auditorWarning).ToContainTextAsync("Supplier ");

        // ---- Daily digest (dev trigger) → captured for the group-scoped auditor ----
        await MailCapture.DrainAsync();
        await Page.GotoAsync($"{BaseUrl}/Dev/RunFreshnessDigest");

        var digests = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.Contains("información regulatoria vencida"));
        var mine = digests.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(auditorEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(mine, Is.Not.Null,
            $"Expected a stale-value digest to the group-scoped auditor {auditorEmail}.");
        // SC-005 — the digest body must name the stale provider, not just carry the subject.
        Assert.That(mine!.HtmlBody + mine.TextBody, Does.Contain("Supplier "));
    }
}
