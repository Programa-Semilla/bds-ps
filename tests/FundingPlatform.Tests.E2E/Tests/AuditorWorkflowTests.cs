using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 040 / US1 (T033) — auditor takes a seeded PendingAudit application through the
/// audit checklist, confirms the (SQL-seeded, Syncfusion-bypassed) PDF, and releases it
/// for signature so the applicant sees the ready-to-sign surface; plus the out-of-group
/// auditor negative (empty inbox + 403 detail).
/// </summary>
[TestFixture]
[Category("AuditorWorkflow")]
public class AuditorWorkflowTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"aw-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    [Test]
    public async Task GoldenPath_AuditorAuditsAndReleases_ApplicantReadyToSign()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, applicantEmail, applicantPassword) =
            await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);

        await FundingAgreementSeeder.SeedPendingAuditApplicationAsync(
            ConnectionString, appId, reviewerUserEmail: $"seed_reviewer_{uniqueId}@example.com");

        // In-scope auditor (RegisterUserAsync assigns all groups; AssignRole keeps them for Auditor).
        var auditorEmail = $"aw_aud_{uniqueId}@example.com";
        await RegisterUserAsync(Page, auditorEmail, Password, "Aud", "Itor", $"AWA-{uniqueId}");
        await AssignRoleAsync(auditorEmail, "Auditor");
        await LoginAsync(Page, auditorEmail, Password);

        // The auditor (Auditor-only → narrowed sidebar) sees the Auditoría inbox nav entry.
        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page.Locator("[data-testid=sidebar-entry-audit-inbox]")).ToBeVisibleAsync();

        // Inbox shows the PendingAudit application.
        await Page.GotoAsync($"{BaseUrl}/Audit");
        await Expect(Page.Locator($"[data-testid=audit-inbox-row][data-application-id='{appId}']")).ToBeVisibleAsync();

        // Open the detail surface: the reviewer-equivalent read (FR-007) shows the
        // applicant summary, the shared decision summary, the per-item provider/quotation
        // detail with the seven-criterion score breakdown, and the review history — not
        // just the thin item table. Then save the audit checklist (all default "Conforme").
        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Expect(Page.Locator("[data-testid=audit-history]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=audit-summary]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=audit-decision-summary]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=audit-items]")).ToBeVisibleAsync();
        // The full provider/quotation table (FR-007 "provider information") must render.
        Assert.That(await Page.Locator("[data-testid=review-quotation-row]").CountAsync(),
            Is.GreaterThan(0), "Auditor must see the provider/quotation rows (FR-007).");
        await Expect(Page.Locator("[data-testid=score-breakdown]").First).ToBeVisibleAsync();
        await Page.Locator("[data-testid=audit-checklist-save]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Simulate the auditor's PDF generation via SQL (the project convention for
        // bypassing Syncfusion in E2E); the auditor then confirms + releases through the UI.
        await FundingAgreementSeeder.SeedGeneratedAgreementAsync(
            ConnectionString, appId, generatedByUserEmail: auditorEmail, CreateBlobServiceClient());

        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Page.Locator("[data-testid=audit-confirm]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Expect(Page.Locator("[data-testid=audit-release]")).ToBeEnabledAsync();
        await Page.Locator("[data-testid=audit-release]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Released → back to the inbox, the row is gone.
        await Page.GotoAsync($"{BaseUrl}/Audit");
        await Expect(Page.Locator($"[data-testid=audit-inbox-row][data-application-id='{appId}']")).ToHaveCountAsync(0);

        // Applicant now sees the ready-to-sign surface (State back to ResponseFinalized + agreement).
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, applicantEmail, applicantPassword);
        await Page.GotoAsync($"{BaseUrl}/ApplicantResponse/Index/{appId}");
        await Expect(Page.Locator("[data-testid=signing-banner-ready]")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Test]
    public async Task OutOfGroupAuditor_SeesEmptyInbox_And403OnDetail()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);
        await FundingAgreementSeeder.SeedPendingAuditApplicationAsync(
            ConnectionString, appId, reviewerUserEmail: $"seed_reviewer_{uniqueId}@example.com");

        // Auditor with NO group memberships (seeded directly, skipping AssignAllGroups).
        var auditorEmail = $"aw_oog_{uniqueId}@example.com";
        await SeedUserWithoutGroupsAsync(auditorEmail, Password, "Out", "Group", $"AWO-{uniqueId}");
        await AssignRoleAsync(auditorEmail, "Auditor");
        await LoginAsync(Page, auditorEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Audit");
        await Expect(Page.Locator($"[data-testid=audit-inbox-row][data-application-id='{appId}']")).ToHaveCountAsync(0);

        var resp = await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        Assert.That(resp!.Status, Is.EqualTo(403), "Out-of-group auditor must be forbidden from the detail page.");
    }

    private async Task SeedUserWithoutGroupsAsync(
        string email, string password, string firstName, string lastName, string legalId)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var qs = $"?email={Uri.EscapeDataString(email)}" +
                 $"&password={Uri.EscapeDataString(password)}" +
                 $"&firstName={Uri.EscapeDataString(firstName)}" +
                 $"&lastName={Uri.EscapeDataString(lastName)}" +
                 $"&legalId={Uri.EscapeDataString(IdentificationData.CedulaFisica(legalId))}";
        var response = await client.GetAsync($"/Account/SeedUser{qs}");
        response.EnsureSuccessStatusCode();
    }
}
