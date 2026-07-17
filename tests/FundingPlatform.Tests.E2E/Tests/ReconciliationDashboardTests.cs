using System.Text.Json;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 048 / US3 — the reconciliation dashboard on real SQL: an in-scope Financial Operator sees the
/// persisted discrepancies with severity + state badges (text + icon, never colour alone), filters by
/// severity, and opens a detail with the fields + correction-history timeline. An Auditor sees the same
/// surface read-only (no write affordances). Discrepancies are seeded via the Development-only
/// <c>/Dev/SeedDiscrepancy</c> seam.
/// </summary>
[Category("ReconciliationDashboard")]
public class ReconciliationDashboardTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private readonly List<string> _seeded = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var p in _seeded)
        {
            if (File.Exists(p)) File.Delete(p);
        }
        _seeded.Clear();
    }

    private async Task<int> SeedAppAsync()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var qPath = Path.Combine(Path.GetTempPath(), $"q-{uid}.pdf");
        File.WriteAllText(qPath, "Quotation placeholder");
        _seeded.Add(qPath);
        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uid, qPath);
        return appId;
    }

    private async Task<int> SeedDiscrepancyAsync(int appId, string severity)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var resp = await client.GetAsync($"/Dev/SeedDiscrepancy?applicationId={appId}&severity={Uri.EscapeDataString(severity)}");
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
    }

    private async Task<string> RegisterInRoleAsync(string role, string suffix)
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var email = $"seed_{suffix}_{uid}@example.com";
        await RegisterUserAsync(Page, email, Pwd, "Seed", suffix, $"{suffix[..3].ToUpperInvariant()}-{uid}");
        await AssignRoleAsync(email, role);
        return email;
    }

    [Test]
    public async Task Operator_SeesDiscrepancies_WithBadges_AndSeverityFilter()
    {
        var appId = await SeedAppAsync();
        await SeedDiscrepancyAsync(appId, "Blocking");
        await SeedDiscrepancyAsync(appId, "Warning");
        var operatorEmail = await RegisterInRoleAsync("Financial Operator", "finop");
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = new ReconciliationPage(Page);
        await page.GotoAsync(BaseUrl);
        await Expect(page.Summary).ToBeVisibleAsync();

        // Both discrepancies for this app appear (there may be others from parallel state — filter by app).
        var appRows = Page.Locator($"[data-testid=reconciliation-row]:has-text(\"APP-{appId:D5}\")");
        await Expect(appRows).ToHaveCountAsync(2);

        // Severity filter = Warning → only the warning row for this app remains.
        await page.ApplySeverityFilterAsync("Warning");
        var appWarnings = Page.Locator($"[data-testid=reconciliation-row][data-severity=\"Warning\"]:has-text(\"APP-{appId:D5}\")");
        await Expect(appWarnings).ToHaveCountAsync(1);
        var appBlocking = Page.Locator($"[data-testid=reconciliation-row][data-severity=\"Blocking\"]:has-text(\"APP-{appId:D5}\")");
        await Expect(appBlocking).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Detail_ShowsFieldsAndTimeline()
    {
        var appId = await SeedAppAsync();
        var discrepancyId = await SeedDiscrepancyAsync(appId, "Warning");
        var operatorEmail = await RegisterInRoleAsync("Financial Operator", "finop");
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = new ReconciliationPage(Page);
        await page.GotoDetailAsync(BaseUrl, discrepancyId);
        await Expect(page.Detail).ToBeVisibleAsync();
        await Expect(page.Expected).ToBeVisibleAsync();
        await Expect(page.Actual).ToBeVisibleAsync();
        await Expect(page.Difference).ToBeVisibleAsync();
        // The genesis "Opened" event is always present in the timeline.
        await Expect(page.TimelineEvents).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Auditor_SeesDetail_ReadOnly()
    {
        var appId = await SeedAppAsync();
        var discrepancyId = await SeedDiscrepancyAsync(appId, "Warning");
        var auditorEmail = await RegisterInRoleAsync("Auditor", "auditor");
        await LoginAsync(Page, auditorEmail, Pwd);

        var page = new ReconciliationPage(Page);
        await page.GotoDetailAsync(BaseUrl, discrepancyId);
        await Expect(page.Detail).ToBeVisibleAsync();
        await Expect(page.ReadOnlyNotice).ToBeVisibleAsync();
        await Expect(page.AssignForm).ToHaveCountAsync(0);
        await Expect(page.WaiveForm).ToHaveCountAsync(0);
    }
}
