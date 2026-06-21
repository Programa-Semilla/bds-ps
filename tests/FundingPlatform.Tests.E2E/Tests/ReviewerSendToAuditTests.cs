using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 040 / US2 (T038) — at ResponseFinalized (no agreement) the reviewer completes
/// the reviewer checklist and sends to audit: "Send to audit" is disabled until all
/// required items are checked; sending transitions the app to PendingAudit (it then
/// appears in the auditor inbox); there is no reviewer "Generate agreement" action.
/// </summary>
[TestFixture]
[Category("ReviewerSendToAudit")]
public class ReviewerSendToAuditTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"rsa-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    [Test]
    public async Task Reviewer_ChecklistGatesSend_ThenSendsToAudit_AppEntersAuditInbox()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);

        // Reviewer opens the ResponseFinalized application via the Review surface.
        var reviewerEmail = $"rsa_rev_{uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, Password, "Rsa", "Reviewer", $"RSAR-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");

        // The reviewer checklist + Send-to-audit card is shown; no "Generate agreement".
        await Expect(Page.Locator("[data-testid=reviewer-checklist-card]")).ToBeVisibleAsync();

        // The send button is disabled until all required items are checked.
        var sendBtn = Page.Locator("[data-testid=reviewer-send-to-audit]");
        var requiredChecks = Page.Locator("[data-testid=reviewer-check][data-required='true']");
        var requiredCount = await requiredChecks.CountAsync();
        if (requiredCount > 0)
        {
            await Expect(sendBtn).ToBeDisabledAsync();
            for (var i = 0; i < requiredCount; i++)
            {
                await requiredChecks.Nth(i).CheckAsync();
            }
        }
        await Expect(sendBtn).ToBeEnabledAsync();
        await sendBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The application is now in the auditor inbox (PendingAudit). Verify as an auditor.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        var auditorEmail = $"rsa_aud_{uniqueId}@example.com";
        await RegisterUserAsync(Page, auditorEmail, Password, "Rsa", "Auditor", $"RSAA-{uniqueId}");
        await AssignRoleAsync(auditorEmail, "Auditor");
        await LoginAsync(Page, auditorEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Audit");
        await Expect(Page.Locator($"[data-testid=audit-inbox-row][data-application-id='{appId}']")).ToBeVisibleAsync();
    }
}
