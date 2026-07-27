using System.Text.Json;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 048 / US2 — the discrepancy lifecycle through the real dashboard on real SQL: assign →
/// under-correction → waive (Warning only) with the correction-history timeline; a Blocking
/// discrepancy cannot be waived (no waive form). Discrepancies are seeded via the Development-only
/// <c>/Dev/SeedDiscrepancy</c> seam so the lifecycle is exercised without constructing the complex
/// underlying warning conditions through the UI.
/// </summary>
[Category("DiscrepancyLifecycle")]
public class DiscrepancyLifecycleTests : AuthenticatedTestBase
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

    private async Task<(int appId, string operatorEmail)> SeedAppAndOperatorAsync()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var qPath = Path.Combine(Path.GetTempPath(), $"q-{uid}.pdf");
        File.WriteAllText(qPath, "Quotation placeholder");
        _seeded.Add(qPath);

        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uid, qPath);
        var operatorEmail = $"seed_finop_{uid}@example.com";
        await RegisterUserAsync(Page, operatorEmail, Pwd, "Fin", "Operator", $"FINOP-{uid}");
        await AssignRoleAsync(operatorEmail, "Financial Operator");
        return (appId, operatorEmail);
    }

    private async Task<int> SeedDiscrepancyAsync(int appId, string severity)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var resp = await client.GetAsync($"/Dev/SeedDiscrepancy?applicationId={appId}&severity={Uri.EscapeDataString(severity)}");
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetInt32();
    }

    [Test]
    public async Task Warning_AssignUnderCorrectionWaive_BuildsTimeline()
    {
        var (appId, operatorEmail) = await SeedAppAndOperatorAsync();
        var discrepancyId = await SeedDiscrepancyAsync(appId, "Warning");
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = new ReconciliationPage(Page);
        await page.GotoDetailAsync(BaseUrl, discrepancyId);
        await Expect(page.Detail).ToBeVisibleAsync();

        // Assign → state Asignada, timeline grows.
        await page.AssignFirstAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("Asignada");

        // Mark under correction.
        await page.MarkUnderCorrectionAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("En corrección");

        // Waive with a reason (Warning only) → state Exonerada.
        await page.WaiveAsync("Aceptada por el operador tras verificación.");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("Exonerada");

        // The correction-history timeline shows the full progression (Opened + 3 transitions).
        await Expect(page.TimelineEvents).ToHaveCountAsync(4);
    }

    [Test]
    public async Task Blocking_CannotBeWaived_NoWaiveForm()
    {
        var (appId, operatorEmail) = await SeedAppAndOperatorAsync();
        var discrepancyId = await SeedDiscrepancyAsync(appId, "Blocking");
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = new ReconciliationPage(Page);
        await page.GotoDetailAsync(BaseUrl, discrepancyId);
        await Expect(page.Detail).ToBeVisibleAsync();

        // A blocking discrepancy can be assigned but never waived.
        await Expect(page.AssignForm).ToBeVisibleAsync();
        await Expect(page.WaiveForm).ToHaveCountAsync(0);
        await Expect(page.RequiredAction).ToContainTextAsync("no se puede exonerar");
    }
}
