using System.Text.Json;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 048 / US4 — assigning a discrepancy sends exactly one branded email to the responsible
/// operator (best-effort, direct-send); detection alone never sends mail. Captured via smtp4dev.
/// The assignee uses an allowlisted <c>@programa-semilla.test</c> address so the send is not dropped.
/// </summary>
[Category("DiscrepancyAssignmentNotification")]
public class DiscrepancyAssignmentNotificationTests : AuthenticatedTestBase
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

    private async Task<int> SeedDiscrepancyAsync(int appId, string severity)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var resp = await client.GetAsync($"/Dev/SeedDiscrepancy?applicationId={appId}&severity={Uri.EscapeDataString(severity)}");
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
    }

    [Test]
    public async Task Assign_SendsExactlyOneBrandedEmail_DetectionSendsNone()
    {
        if (MailCapture is null)
        {
            Assert.Ignore("Mail capture (smtp4dev) not available in this environment.");
            return;
        }

        var uid = Guid.NewGuid().ToString("N")[..8];
        var qPath = Path.Combine(Path.GetTempPath(), $"q-{uid}.pdf");
        File.WriteAllText(qPath, "Quotation placeholder");
        _seeded.Add(qPath);

        var (appId, _, _) = await CreateApplicationAndSubmitResponseAsync(uid, qPath);

        // The acting + assignee operator uses an allowlisted address so the send is captured. A unique
        // last name makes its option identifiable among all Financial Operators (parallel-test-safe).
        var operatorEmail = $"finop_{uid}@programa-semilla.test";
        var operatorLabel = $"Fin Op{uid}";
        await RegisterUserAsync(Page, operatorEmail, Pwd, "Fin", $"Op{uid}", $"FINOP-{uid}");
        await AssignRoleAsync(operatorEmail, "Financial Operator");

        // Detection alone (seed) must not send mail.
        var discrepancyId = await SeedDiscrepancyAsync(appId, "Warning");
        await MailCapture.DrainAsync();

        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new ReconciliationPage(Page);
        await page.GotoDetailAsync(BaseUrl, discrepancyId);
        await Expect(page.Detail).ToBeVisibleAsync();

        await page.AssignToLabelAsync(operatorLabel);
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        var messages = await MailCapture.WaitForAsync(
            minCount: 1,
            timeout: TimeSpan.FromSeconds(20),
            filter: m => m.Subject.Contains("diferencia", StringComparison.OrdinalIgnoreCase));
        var toOperator = messages
            .Where(m => m.ToAddresses.Any(t => t.Contains(operatorEmail, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.That(toOperator, Has.Count.EqualTo(1), "exactly one assignment email to the operator");
        // Branded shell: the partner-footer logo host is absolute (never localhost:5000) + ALIA copy.
        Assert.That(toOperator[0].HtmlBody, Does.Contain("Diferencia asignada"));
    }
}
