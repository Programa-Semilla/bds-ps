using System.Globalization;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 045 / US2 — the five-dimension participant balance reconciles exactly to its
/// definitions at every step (SC-004): <c>Available = Allocated − Paid</c>,
/// <c>Paid = Validated + Pending</c>, and validation does not change Available.
/// </summary>
[Category("ParticipantBalance")]
public class ParticipantBalanceTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string Today = "2026-07-15";
    private string _pdf = string.Empty;
    private readonly List<string> _seeded = [];

    [SetUp]
    public void SetUp()
    {
        _pdf = Path.Combine(Path.GetTempPath(), $"disb-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_pdf, "%PDF-1.4\ndisbursement evidence\n%%EOF\n"u8.ToArray());
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

    private static decimal ParseCrc(string s)
    {
        // es-CR currency: '.' thousands, ',' decimal → strip symbol, drop thousands, dot the decimal.
        var kept = new string(s.Where(c => char.IsDigit(c) || c == ',' || c == '.').ToArray());
        kept = kept.Replace(".", "").Replace(",", ".");
        return string.IsNullOrEmpty(kept) ? 0m : decimal.Parse(kept, CultureInfo.InvariantCulture);
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

    private async Task AssertBalanceAsync(DisbursementPage page, int appId,
        decimal allocated, decimal paid, decimal validated, decimal pending, decimal available)
    {
        await page.GotoAsync(BaseUrl, appId);
        var actualAllocated = ParseCrc(await page.BalanceText("allocated"));
        var actualPaid = ParseCrc(await page.BalanceText("paid"));
        var actualValidated = ParseCrc(await page.BalanceText("validated"));
        var actualPending = ParseCrc(await page.BalanceText("pending"));
        var actualAvailable = ParseCrc(await page.BalanceText("available"));
        Assert.Multiple(() =>
        {
            Assert.That(actualAllocated, Is.EqualTo(allocated), "Allocated");
            Assert.That(actualPaid, Is.EqualTo(paid), "Paid");
            Assert.That(actualValidated, Is.EqualTo(validated), "Validated");
            Assert.That(actualPending, Is.EqualTo(pending), "Pending");
            Assert.That(actualAvailable, Is.EqualTo(available), "Available");
        });
    }

    [Test]
    public async Task FiveDimensions_ReconcileExactly_AsDisbursementsRecordedAndValidated()
    {
        var (appId, operatorEmail) = await SeedAsync(1_000_000m);
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = new DisbursementPage(Page);

        // Nothing disbursed yet.
        await AssertBalanceAsync(page, appId, allocated: 1_000_000m, paid: 0m, validated: 0m, pending: 0m, available: 1_000_000m);

        // Record ₡300,000 (not yet validated) → Paid & Pending move, Validated stays 0.
        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, 300_000m, "TX-1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await AssertBalanceAsync(page, appId, allocated: 1_000_000m, paid: 300_000m, validated: 0m, pending: 300_000m, available: 700_000m);

        // Prove + validate it → moves from Pending to Validated; Paid & Available unchanged.
        await page.GotoAsync(BaseUrl, appId);
        await page.OpenFirstAsync();
        await Expect(page.Detail).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("BankReceipt", 300_000m, "BR-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("Invoice", 300_000m, "IV-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.ValidateButton).ToBeEnabledAsync();
        await page.ValidateAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        await AssertBalanceAsync(page, appId, allocated: 1_000_000m, paid: 300_000m, validated: 300_000m, pending: 0m, available: 700_000m);
    }
}
