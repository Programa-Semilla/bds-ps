using System.Globalization;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 045 / US3 — several disbursements per agreement (partial payments); the total may
/// not exceed the allocation (SC-005, FR-020). Over-disbursement goes blocking and
/// <c>Available</c> reads legibly negative.
/// </summary>
[Category("DisbursementPartialAndOver")]
public class DisbursementPartialAndOverTests : AuthenticatedTestBase
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
        var kept = new string(s.Where(c => char.IsDigit(c) || c == ',' || c == '.' || c == '-').ToArray());
        var negative = kept.StartsWith('-');
        kept = kept.Replace("-", "").Replace(".", "").Replace(",", ".");
        var value = string.IsNullOrEmpty(kept) ? 0m : decimal.Parse(kept, CultureInfo.InvariantCulture);
        return negative ? -value : value;
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

    private async Task RecordProveValidateAsync(DisbursementPage page, int appId, decimal amount)
    {
        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, amount, $"TX-{amount}");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.OpenFirstAsync();
        await Expect(page.Detail).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("BankReceipt", amount, "BR", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("Invoice", amount, "IV", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.ValidateButton).ToBeEnabledAsync();
        await page.ValidateAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
    }

    [Test]
    public async Task PartialPayments_SumWithinTotal_Succeed_AvailableToZero()
    {
        var (appId, operatorEmail) = await SeedAsync(1_000_000m);
        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new DisbursementPage(Page);

        await RecordProveValidateAsync(page, appId, 600_000m);
        await RecordProveValidateAsync(page, appId, 400_000m);

        await page.GotoAsync(BaseUrl, appId);
        Assert.That(ParseCrc(await page.BalanceText("available")), Is.EqualTo(0m));
        Assert.That(ParseCrc(await page.BalanceText("validated")), Is.EqualTo(1_000_000m));
        await Expect(page.Rows).ToHaveCountAsync(2);
    }

    [Test]
    public async Task OverDisbursement_Blocked_AvailableGoesNegative()
    {
        var (appId, operatorEmail) = await SeedAsync(1_000_000m);
        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new DisbursementPage(Page);

        // First ₡600,000 fits under the ceiling.
        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, 600_000m, "TX-1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // A second ₡500,000 crosses the ₡1,000,000 ceiling → the crossing disbursement is
        // blocked as an over-disbursement and Available goes negative.
        await page.RecordAsync(Today, 500_000m, "TX-2");
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // Available is legibly negative (−₡100,000), never clamped.
        Assert.That(ParseCrc(await page.BalanceText("available")), Is.EqualTo(-100_000m));
        await Expect(page.OverDisbursedBanner).ToBeVisibleAsync();

        // The crossing (newest) disbursement is Inconsistent and cannot validate.
        await page.OpenFirstAsync();
        await Expect(page.Detail).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("Inconsistente");
        await Expect(page.Discrepancies).ToContainTextAsync("aprobado");
        await Expect(page.ValidateButton).ToBeDisabledAsync();
    }
}
