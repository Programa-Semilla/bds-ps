using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 047 / US4 — the append-only evidence version chain over real SQL: replacing a file (or a
/// reconciliation-critical field) appends a version, retains the prior as superseded, and both
/// versions are downloadable. Exercises the filtered one-current unique index on real SQL.
/// </summary>
[Category("EvidenceVersionHistory")]
public class EvidenceVersionHistoryTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string Today = "2026-07-15";
    private string _pdfV1 = string.Empty;
    private string _pdfV2 = string.Empty;
    private readonly List<string> _seeded = [];

    [SetUp]
    public void SetUp()
    {
        _pdfV1 = Path.Combine(Path.GetTempPath(), $"ev1-{Guid.NewGuid():N}.pdf");
        _pdfV2 = Path.Combine(Path.GetTempPath(), $"ev2-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_pdfV1, "%PDF-1.4\nversion one\n%%EOF\n"u8.ToArray());
        File.WriteAllBytes(_pdfV2, "%PDF-1.4\nversion two corrected\n%%EOF\n"u8.ToArray());
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var p in new[] { _pdfV1, _pdfV2 }.Concat(_seeded))
        {
            if (File.Exists(p)) File.Delete(p);
        }
        _seeded.Clear();
    }

    private async Task<(int appId, string finopEmail)> SeedAsync()
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
        return (appId, finopEmail);
    }

    [Test]
    public async Task Replace_AppendsVersion_BothViewableAndDownloadable()
    {
        var (appId, finopEmail) = await SeedAsync();
        await LoginAsync(Page, finopEmail, Pwd);
        var page = new EvidencePage(Page);

        await page.GotoAsync(BaseUrl, appId);
        // Allocate 300k of the 400k invoice, so a later amount-reduction to 350k still satisfies
        // Σ allocations ≤ amount (FR-005 — the reconciliation-critical edit guard).
        await page.AttachAsync("Invoice", 400_000m, "F-001", Today, _pdfV1, 300_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        await page.OpenFirstAsync();
        await Expect(page.Detail).ToBeVisibleAsync();
        await Expect(page.VersionRows).ToHaveCountAsync(1);

        // Replace with a corrected file + a lower (but still ≥ allocated) amount + a reason → v2 appends.
        await page.ReplaceAsync(350_000m, "F-001-B", Today, "monto corregido", _pdfV2);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.VersionRows).ToHaveCountAsync(2);

        // Exactly one current version (the filtered one-current index holds on real SQL).
        await Expect(Page.Locator("[data-testid=evidence-version-row] .badge:has-text('Versión actual')"))
            .ToHaveCountAsync(1);

        // Both versions download (v1 original + v2 current).
        var v1 = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.Locator("[data-testid=evidence-version-row][data-version='1'] [data-testid=evidence-version-download]").ClickAsync());
        Assert.That(await ReadAllAsync(v1), Does.Contain("version one"));

        var v2 = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.Locator("[data-testid=evidence-version-row][data-version='2'] [data-testid=evidence-version-download]").ClickAsync());
        Assert.That(await ReadAllAsync(v2), Does.Contain("version two corrected"));
    }

    private static async Task<string> ReadAllAsync(IDownload download)
    {
        var path = await download.PathAsync();
        return path is null ? string.Empty : await File.ReadAllTextAsync(path);
    }
}
