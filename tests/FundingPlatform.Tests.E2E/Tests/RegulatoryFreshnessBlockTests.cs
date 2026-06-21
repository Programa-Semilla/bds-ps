using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 043 / US1 (T008) — the auditor cannot advance an application through the
/// audit stage while a relied-on (selected) provider has a stale or never-reviewed
/// required regulatory field. The block names provider + field + last-reviewed
/// (FR-007); re-authorizing (refreshing the provider) clears it (FR-008); the
/// confirm/release paths enforce the same gate server-side (FR-009).
///
/// The freshness gate keys off slice-A-maintained timestamps, so no Hacienda sync is
/// needed — the selected suppliers created by the seed flow are never-reviewed
/// (already stale), and <c>FundingAgreementSeeder.SetSelectedSuppliersRegulatory*</c>
/// stamps them fresh/stale via SQL.
/// </summary>
[TestFixture]
[Category("RegulatoryFreshnessBlock")]
public class RegulatoryFreshnessBlockTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"rfb-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    private async Task<(int appId, string auditorEmail)> ArriveAtAuditAsync(string tag)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uniqueId, _quotationFilePath);
        await FundingAgreementSeeder.SeedPendingAuditApplicationAsync(
            ConnectionString, appId, reviewerUserEmail: $"seed_reviewer_{uniqueId}@example.com");

        var auditorEmail = $"{tag}_{uniqueId}@example.com";
        await RegisterUserAsync(Page, auditorEmail, Password, "Aud", "Itor", $"{tag.ToUpperInvariant()}-{uniqueId}");
        await AssignRoleAsync(auditorEmail, "Auditor");
        await LoginAsync(Page, auditorEmail, Password);

        // Record the audit checklist as compliant so CanGenerate/CanConfirm are satisfied.
        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Page.Locator("[data-testid=audit-checklist-save]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return (appId, auditorEmail);
    }

    [Test]
    public async Task NeverReviewedSupplier_BlocksGenerate_MessageNamesProviderAndField()
    {
        var (appId, _) = await ArriveAtAuditAsync("rfb_gen");

        // Selected supplier is never-reviewed (all required fields stale) → generate blocked.
        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Page.Locator("[data-testid=audit-generate]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var err = Page.Locator("[data-testid=audit-fa-error]");
        await Expect(err).ToBeVisibleAsync();
        await Expect(err).ToContainTextAsync("información regulatoria");
        await Expect(err).ToContainTextAsync("sin revisar");

        // No agreement was produced — generate remains available (still PendingAudit).
        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Expect(Page.Locator("[data-testid=audit-generate]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=audit-confirm]")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task StaleByDate_BlocksConfirm_ThenFreshClears_ReleaseSucceeds()
    {
        var (appId, auditorEmail) = await ArriveAtAuditAsync("rfb_conf");

        // Stale-by-date suppliers (90d > 30d window) + a seeded agreement → confirm blocked.
        await FundingAgreementSeeder.SetSelectedSuppliersRegulatoryAsync(ConnectionString, appId, daysAgo: 90);
        await FundingAgreementSeeder.SeedGeneratedAgreementAsync(
            ConnectionString, appId, generatedByUserEmail: auditorEmail, CreateBlobServiceClient());

        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Page.Locator("[data-testid=audit-confirm]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var err = Page.Locator("[data-testid=audit-toast-error]");
        await Expect(err).ToBeVisibleAsync();
        await Expect(err).ToContainTextAsync("revisado por última vez el");

        // Re-authorize (refresh the providers) clears the block (FR-008).
        await FundingAgreementSeeder.SetSelectedSuppliersRegulatoryFreshAsync(ConnectionString, appId);

        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Page.Locator("[data-testid=audit-confirm]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator("[data-testid=audit-toast-success]")).ToBeVisibleAsync();

        await Page.GotoAsync($"{BaseUrl}/Audit/{appId}");
        await Page.Locator("[data-testid=audit-release]").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Released → application left the audit inbox.
        await Page.GotoAsync($"{BaseUrl}/Audit");
        await Expect(Page.Locator($"[data-testid=audit-inbox-row][data-application-id='{appId}']"))
            .ToHaveCountAsync(0);
    }
}
