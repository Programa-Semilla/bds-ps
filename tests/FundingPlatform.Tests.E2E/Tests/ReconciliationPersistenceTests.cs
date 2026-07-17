using System.Text.Json;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 048 / US1 — persisted discrepancies with fixed severity on real SQL: a blocking discrepancy
/// persists with a "Bloqueante" severity badge + "Abierta" lifecycle badge and blocks validation; a
/// Warning discrepancy is persisted but never blocks the money gate. (Clean → none and auto-resolve
/// are additionally covered by <c>DisbursementReconciliationTests</c>, which drives the same persisted
/// <c>_DiscrepancyList</c>.)
/// </summary>
[Category("ReconciliationPersistence")]
public class ReconciliationPersistenceTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string Today = "2026-07-15";
    private string _pdf = string.Empty;
    private readonly List<string> _seeded = [];

    [SetUp]
    public void SetUp()
    {
        _pdf = Path.Combine(Path.GetTempPath(), $"rp-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_pdf, "%PDF-1.4\nreconciliation evidence\n%%EOF\n"u8.ToArray());
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in new[] { _pdf }.Concat(_seeded))
        {
            if (File.Exists(p)) File.Delete(p);
        }
        _seeded.Clear();
    }

    private async Task<(int appId, string operatorEmail)> SeedAsync(decimal allocation)
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var qPath = Path.Combine(Path.GetTempPath(), $"q-{uid}.pdf");
        File.WriteAllText(qPath, "Quotation placeholder");
        _seeded.Add(qPath);

        var (appId, applicantEmail, _) = await CreateApplicationAndSubmitResponseAsync(uid, qPath);
        var reviewerEmail = $"seed_reviewer_{uid}@example.com";
        var adminEmail = $"seed_admin_{uid}@example.com";
        _seeded.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, adminEmail, applicantEmail, reviewerEmail, CreateBlobServiceClient()));

        var operatorEmail = $"seed_finop_{uid}@example.com";
        await RegisterUserAsync(Page, operatorEmail, Pwd, "Fin", "Operator", $"FINOP-{uid}");
        await AssignRoleAsync(operatorEmail, "Financial Operator");
        await DisbursementSeeder.SeedAllocationAsync(ConnectionString, appId, allocation, adminEmail);

        return (appId, operatorEmail);
    }

    private async Task<int> SeedWarningAsync(int appId)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var resp = await client.GetAsync($"/Dev/SeedDiscrepancy?applicationId={appId}&severity=Warning");
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
    }

    [Test]
    public async Task BlockingDiscrepancy_PersistsWithSeverityBadge_AndBlocksValidation()
    {
        var (appId, operatorEmail) = await SeedAsync(100_000m);
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = new DisbursementPage(Page);
        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, 85_800m, "TX-1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.OpenFirstAsync();
        await Expect(page.Detail).ToBeVisibleAsync();

        await page.AttachEvidenceAsync("BankReceipt", 85_800m, "BR-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("Invoice", 85_728m, "IV-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // The persisted discrepancy carries the fixed Blocking severity + Open lifecycle state.
        await Expect(page.DiscrepancyItems).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid=discrepancy-severity]")).ToContainTextAsync("Bloqueante");
        await Expect(Page.Locator("[data-testid=discrepancy-state]")).ToContainTextAsync("Abierta");
        await Expect(page.ValidateButton).ToBeDisabledAsync();
    }

    [Test]
    public async Task WarningDiscrepancy_IsPersisted_AndSurfacedAsNonBlocking()
    {
        var (appId, operatorEmail) = await SeedAsync(100_000m);
        var discrepancyId = await SeedWarningAsync(appId);
        await LoginAsync(Page, operatorEmail, Pwd);

        // The persisted Warning is surfaced with its non-blocking severity label. Warnings are never
        // part of the money gate (DisbursementReconciliation only computes blocking legs), so a Warning
        // cannot block validation by construction; here we assert it persists + is labelled correctly.
        var page = new ReconciliationPage(Page);
        await page.GotoDetailAsync(BaseUrl, discrepancyId);
        await Expect(page.Detail).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=reconciliation-detail][data-severity=\"Warning\"]")).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("Abierta");
        await Expect(page.WaiveForm).ToBeVisibleAsync(); // a Warning IS waivable (unlike Blocking)
    }
}
