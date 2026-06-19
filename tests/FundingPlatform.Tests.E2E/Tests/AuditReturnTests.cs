using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 040 / US3 (T044) — the auditor marks an item non-compliant and returns the
/// application to the reviewer; the reviewer sees the findings, reworks, and re-sends to
/// audit (the PendingAudit ⇄ ReturnedFromAudit loop).
/// </summary>
[TestFixture]
[Category("AuditReturn")]
public class AuditReturnTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"ar-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    [Test]
    public async Task AuditorReturns_ReviewerSeesFindingsAndResends_LoopsBackToAuditInbox()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);
        await FundingAgreementSeeder.SeedPendingAuditApplicationAsync(
            ConnectionString, appId, reviewerUserEmail: $"seed_reviewer_{uniqueId}@example.com");

        // Auditor marks the first checklist item non-compliant with a reason, saves, returns.
        var auditorEmail = $"ar_aud_{uniqueId}@example.com";
        await RegisterUserAsync(Page, auditorEmail, Password, "Ar", "Auditor", $"ARA-{uniqueId}");
        await AssignRoleAsync(auditorEmail, "Auditor");
        await LoginAsync(Page, auditorEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        var firstItem = Page.Locator("[data-testid=audit-checklist-item]").First;
        await firstItem.Locator("[data-testid=audit-mark-noncompliant]").CheckAsync();
        await firstItem.Locator("[data-testid=audit-mark-reason]").FillAsync("Falta el documento X.");

        // Spec 040 — the actions react live to the checklist (no save round-trip needed):
        // a non-compliant item with a reason disables "Aprobar" and enables "Devolver".
        await Expect(Page.Locator("[data-testid=audit-approve]")).ToBeDisabledAsync();
        await Expect(Page.Locator("[data-testid=audit-return]")).ToBeEnabledAsync();

        // Returning submits the live marks (one click persists the reason + returns).
        await Page.Locator("[data-testid=audit-return]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Returned → gone from the auditor inbox.
        await Page.GotoAsync($"{BaseUrl}/Audit");
        await Expect(Page.Locator($"[data-testid=audit-inbox-row][data-application-id='{appId}']")).ToHaveCountAsync(0);

        // Reviewer sees the findings + re-completes the checklist + re-sends.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        var reviewerEmail = $"ar_rev_{uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, Password, "Ar", "Reviewer", $"ARR-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, Password);

        // Regression — a returned (ReturnedFromAudit) application must reappear on the
        // reviewer's queue worklist (/Review, "Cola de revisión"), not only via the email
        // deep link. The shipped bug hid it because the queue projection only fetched
        // Submitted/UnderReview/Resolved and never ReturnedFromAudit.
        await Page.GotoAsync($"{BaseUrl}/Review");
        await Expect(Page.Locator(
            $"[data-testid=reviewer-queue-row][data-application-number='APP-{appId:D5}']")).ToBeVisibleAsync();

        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        await Expect(Page.Locator("[data-testid=audit-findings-card]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=audit-finding]").First).ToContainTextAsync("Falta el documento X.");

        var requiredChecks = Page.Locator("[data-testid=reviewer-check][data-required='true']");
        var requiredCount = await requiredChecks.CountAsync();
        for (var i = 0; i < requiredCount; i++)
        {
            await requiredChecks.Nth(i).CheckAsync();
        }
        await Page.Locator("[data-testid=reviewer-send-to-audit]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Back in the auditor inbox (PendingAudit again).
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, auditorEmail, Password);
        await Page.GotoAsync($"{BaseUrl}/Audit");
        await Expect(Page.Locator($"[data-testid=audit-inbox-row][data-application-id='{appId}']")).ToBeVisibleAsync();
    }
}
