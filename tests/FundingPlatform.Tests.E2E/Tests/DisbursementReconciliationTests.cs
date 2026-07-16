using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 045 / US1 — record → prove → reconcile to the colón (AC-001, AC-005).
/// Reuses <see cref="FundingAgreementSeeder.SeedExecutedAgreementAsync"/> to reach the
/// executed gate and <see cref="DisbursementSeeder.SeedAllocationAsync"/> for a legible
/// ₡100,000 allocation.
/// </summary>
[Category("DisbursementReconciliation")]
public class DisbursementReconciliationTests : AuthenticatedTestBase
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

    /// <summary>Records a disbursement and opens its detail; returns the disbursement page on Detail.</summary>
    private async Task<DisbursementPage> RecordAndOpenAsync(int appId, decimal amount)
    {
        var page = new DisbursementPage(Page);
        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, amount, "TX-REF-1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.OpenFirstAsync();
        await Expect(page.Detail).ToBeVisibleAsync();
        return page;
    }

    [Test]
    public async Task RecordWithMismatchedInvoice_FlagsColonDiscrepancy_BlocksValidation()
    {
        var (appId, operatorEmail) = await SeedAsync(100_000m);
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = await RecordAndOpenAsync(appId, 85_800m);

        await page.AttachEvidenceAsync("BankReceipt", 85_800m, "BR-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("Invoice", 85_728m, "IV-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // ₡72 discrepancy attributed to the factura; state Inconsistente; Validar refused.
        await Expect(page.Discrepancies).ToBeVisibleAsync();
        await Expect(page.DiscrepancyItems).ToHaveCountAsync(1);
        await Expect(page.DiscrepancyItems.First).ToContainTextAsync("72");
        await Expect(page.DiscrepancyItems.First).ToContainTextAsync("factura");
        await Expect(page.DetailState).ToContainTextAsync("Inconsistente");
        await Expect(page.ValidateButton).ToBeDisabledAsync();
    }

    [Test]
    public async Task MissingInvoice_CannotValidate_ShowsMissing()
    {
        var (appId, operatorEmail) = await SeedAsync(100_000m);
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = await RecordAndOpenAsync(appId, 50_000m);
        await page.AttachEvidenceAsync("BankReceipt", 50_000m, "BR-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // The invoice is missing → Validar is disabled and the invoice block reads "missing".
        await Expect(page.EvidenceMissing("Invoice")).ToBeVisibleAsync();
        await Expect(page.ValidateButton).ToBeDisabledAsync();

        // Force the server-side completeness gate (remove the disabled attr, then click):
        // the system refuses and states which document is missing (es-CR).
        await page.ValidateButton.EvaluateAsync("el => el.removeAttribute('disabled')");
        await page.ValidateButton.ClickAsync();
        await Expect(page.ErrorToast).ToBeVisibleAsync();
        await Expect(page.ErrorToast).ToContainTextAsync("factura");
    }

    [Test]
    public async Task CorrectInvoice_ClearsDiscrepancy_AllowsValidation()
    {
        var (appId, operatorEmail) = await SeedAsync(100_000m);
        await LoginAsync(Page, operatorEmail, Pwd);

        var page = await RecordAndOpenAsync(appId, 85_800m);
        await page.AttachEvidenceAsync("BankReceipt", 85_800m, "BR-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("Invoice", 85_728m, "IV-1", Today, _pdf);
        await Expect(page.DiscrepancyItems).ToHaveCountAsync(1);

        // Correct the invoice amount → reconciliation re-runs, discrepancy clears automatically.
        await page.AttachEvidenceAsync("Invoice", 85_800m, "IV-1b", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.NoDiscrepancies).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("Registrado");
        await Expect(page.ValidateButton).ToBeEnabledAsync();

        // Validate → state Validado; locked notice appears; edit/validate controls gone.
        await page.ValidateAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.DetailState).ToContainTextAsync("Validado");
        await Expect(page.LockedNotice).ToBeVisibleAsync();
        await Expect(page.ValidateButton).ToHaveCountAsync(0);
    }
}
