using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 046 / US3 — the Financial Operator attributes a disbursement to a committed budget-line: a
/// mismatched split is rejected, a matching split records, over-paying a line blocks Validar, and the
/// over-paid line shows a negative Available (never clamped).
/// </summary>
[Category("LineAttribution")]
public class LineAttributionTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string Today = "2026-07-15";
    private string _pdf = string.Empty;
    private readonly List<string> _seeded = [];

    [SetUp]
    public void SetUp()
    {
        _pdf = Path.Combine(Path.GetTempPath(), $"la-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_pdf, "%PDF-1.4\nline attribution evidence\n%%EOF\n"u8.ToArray());
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

    [Test]
    public async Task Split_MustMatch_OverpaymentBlocksValidar_NegativeAvailableVisible()
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
        await DisbursementSeeder.SeedAllocationAsync(ConnectionString, appId, 1_000_000m, adminEmail);

        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new DisbursementPage(Page);
        await page.GotoAsync(BaseUrl, appId);

        // Commit the single line so it can accept an attribution.
        await Page.Locator("[data-testid=line-commit-btn]").First.ClickAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // Mismatched split (amount 2000, split 1000) → rejected.
        await RecordWithSplitAsync("2000", "TX-1", "1000");
        await Expect(page.ErrorToast).ToBeVisibleAsync();

        // Matching split (amount 2000, split 2000) → recorded. The single line budget is well under
        // 2000, so the line is over-paid: Available goes negative (never clamped).
        await RecordWithSplitAsync("2000", "TX-2", "2000");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=line-available]").First).ToHaveClassAsync(new Regex("text-danger"));

        // Prove the disbursement, then Validar is blocked by the per-line over-payment gate.
        await page.OpenFirstAsync();
        await page.AttachEvidenceAsync("BankReceipt", 2000m, "BR-1", Today, _pdf);
        await page.AttachEvidenceAsync("Invoice", 2000m, "IV-1", Today, _pdf);
        await page.ValidateAsync();
        await Expect(page.ErrorToast).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("Registrado"); // not validated
    }

    /// <summary>Fills the Record form's amount + bank-txn and the single committed line's split amount,
    /// then submits.</summary>
    private async Task RecordWithSplitAsync(string amount, string bankTxn, string splitAmount)
    {
        await Page.Locator("[data-testid=disbursement-payment-date]").FillAsync(Today);
        await Page.Locator("[data-testid=disbursement-amount]").FillAsync(amount);
        await Page.Locator("[data-testid=disbursement-bank-txn]").FillAsync(bankTxn);
        await Page.Locator("[data-testid=split-amount]").First.FillAsync(splitAmount);
        await Page.Locator("[data-testid=disbursement-record-submit]").ClickAsync();
    }
}
