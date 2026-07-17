using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 047 / US1 — the evidence graph + per-line allocation over real SQL: attach a typed evidence
/// document, allocate it to a budget-line, over-allocation refused, orphan refused, payment-
/// independent acceptance stored. Plus SC-007 cross-cutting: Auditor read-only → 403 on write.
/// Exercises the TINYINT EvidenceType materialization, the one-current version index, and the M:N
/// allocation cascade on real SQL (which InMemory can't prove).
/// </summary>
[Category("EvidenceGraphAllocation")]
public class EvidenceGraphAllocationTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string Today = "2026-07-15";
    private string _pdf = string.Empty;
    private readonly List<string> _seeded = [];

    [SetUp]
    public void SetUp()
    {
        _pdf = Path.Combine(Path.GetTempPath(), $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_pdf, "%PDF-1.4\nevidence body\n%%EOF\n"u8.ToArray());
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

    private async Task<(int appId, string finopEmail, string auditorEmail)> SeedAsync()
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

        var finopEmail = $"seed_finop_{uid}@example.com";
        await RegisterUserAsync(Page, finopEmail, Pwd, "Fin", "Operator", $"FINOP-{uid}");
        await AssignRoleAsync(finopEmail, "Financial Operator");

        var auditorEmail = $"seed_auditor_{uid}@example.com";
        await RegisterUserAsync(Page, auditorEmail, Pwd, "Aud", "Itor", $"AUD-{uid}");
        await AssignRoleAsync(auditorEmail, "Auditor");

        return (appId, finopEmail, auditorEmail);
    }

    [Test]
    public async Task Attach_Allocate_Over_Orphan_Acceptance()
    {
        var (appId, finopEmail, _) = await SeedAsync();
        await LoginAsync(Page, finopEmail, Pwd);
        var page = new EvidencePage(Page);
        await page.GotoAsync(BaseUrl, appId);
        await Expect(page.Surface).ToBeVisibleAsync();

        // Attach an Invoice allocated to the single seed line → success, one row.
        await page.AttachAsync("Invoice", 100_000m, "F-001", Today, _pdf, 100_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.Rows).ToHaveCountAsync(1);

        // Over-allocation: amount 100,000 but allocate 200,000 → refused.
        await page.AttachAsync("Invoice", 100_000m, "F-002", Today, _pdf, 200_000m);
        await Expect(page.ErrorToast).ToBeVisibleAsync();
        await Expect(page.Rows).ToHaveCountAsync(1); // not added

        // Orphan: no line amount + no disbursement → refused.
        await page.AttachAsync("Invoice", 100_000m, "F-003", Today, _pdf, (decimal?)null);
        await Expect(page.ErrorToast).ToBeVisibleAsync();
        await Expect(page.Rows).ToHaveCountAsync(1);

        // Payment-independent Signed Acceptance allocated to the line → stored.
        await page.AttachAsync("SignedAcceptance", 100_000m, "ACT-1", Today, _pdf, 100_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.Rows).ToHaveCountAsync(2);
    }

    [Test]
    public async Task Auditor_IsReadOnly_CannotAttach()
    {
        var (appId, finopEmail, auditorEmail) = await SeedAsync();

        // A Financial Operator first stores one evidence so the read surface is non-empty.
        await LoginAsync(Page, finopEmail, Pwd);
        var page = new EvidencePage(Page);
        await page.GotoAsync(BaseUrl, appId);
        await page.AttachAsync("Invoice", 100_000m, "F-001", Today, _pdf, 100_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // The Auditor can READ the evidence list but the write form is not rendered (CanWrite=false).
        await LoginAsync(Page, auditorEmail, Pwd);
        await page.GotoAsync(BaseUrl, appId);
        await Expect(page.Surface).ToBeVisibleAsync();
        await Expect(page.Rows).ToHaveCountAsync(1);
        await Expect(page.AttachForm).ToHaveCountAsync(0); // no write form for the Auditor
    }
}
