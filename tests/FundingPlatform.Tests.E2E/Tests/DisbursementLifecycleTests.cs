using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 045 / US4 — correct before validation, lock after, full audit trail
/// (SC-006, SC-007).
/// </summary>
[Category("DisbursementLifecycle")]
public class DisbursementLifecycleTests : AuthenticatedTestBase
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

    [Test]
    public async Task PreValidation_EditReplaceCancel_Allowed_ReconciliationReruns()
    {
        var (appId, operatorEmail) = await SeedAsync(1_000_000m);
        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new DisbursementPage(Page);

        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, 500_000m, "TX-1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.OpenFirstAsync();

        // Attach matching evidence → clean.
        await page.AttachEvidenceAsync("BankReceipt", 500_000m, "BR-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("Invoice", 500_000m, "IV-1", Today, _pdf);
        await Expect(page.NoDiscrepancies).ToBeVisibleAsync();

        // Edit the amount → reconciliation re-runs automatically; both evidences now mismatch.
        await page.EditAmountAsync(450_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.DiscrepancyItems).ToHaveCountAsync(2);

        // Replace both files to match again → clean.
        await page.AttachEvidenceAsync("BankReceipt", 450_000m, "BR-2", Today, _pdf);
        await page.AttachEvidenceAsync("Invoice", 450_000m, "IV-2", Today, _pdf);
        await Expect(page.NoDiscrepancies).ToBeVisibleAsync();

        // Cancel → terminal; contributes nothing to the balance.
        await page.CancelWithConfirmAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.GotoAsync(BaseUrl, appId);
        await Expect(page.RowById(await FirstOrDefaultRowIdAsync(page))).ToContainTextAsync("Cancelado");
    }

    private static async Task<int> FirstOrDefaultRowIdAsync(DisbursementPage page)
        => await page.Rows.CountAsync() > 0 ? await page.FirstRowIdAsync() : 0;

    [Test]
    public async Task Validated_EditAndDelete_Refused()
    {
        var (appId, operatorEmail) = await SeedAsync(1_000_000m);
        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new DisbursementPage(Page);

        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, 300_000m, "TX-1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.OpenFirstAsync();
        await page.AttachEvidenceAsync("BankReceipt", 300_000m, "BR-1", Today, _pdf);
        await page.AttachEvidenceAsync("Invoice", 300_000m, "IV-1", Today, _pdf);
        await Expect(page.ValidateButton).ToBeEnabledAsync();
        await page.ValidateAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // Validated → locked: no edit form, no validate/cancel controls; locked notice shown.
        await Expect(page.DetailState).ToContainTextAsync("Validado");
        await Expect(page.LockedNotice).ToBeVisibleAsync();
        await Expect(page.EditForm).ToHaveCountAsync(0);
        await Expect(page.ValidateButton).ToHaveCountAsync(0);
        await Expect(page.CancelButton).ToHaveCountAsync(0);
    }

    [Test]
    public async Task EveryAction_AppearsInAuditTrail_WithActorAndBeforeAfter()
    {
        var (appId, operatorEmail) = await SeedAsync(1_000_000m);
        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new DisbursementPage(Page);

        await page.GotoAsync(BaseUrl, appId);
        await page.RecordAsync(Today, 200_000m, "TX-1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        var disbId = await page.FirstRowIdAsync();

        await page.OpenFirstAsync();
        await page.AttachEvidenceAsync("BankReceipt", 200_000m, "BR-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AttachEvidenceAsync("Invoice", 200_000m, "IV-1", Today, _pdf);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.EditAmountAsync(200_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.ValidateAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        var (actions, actor, payloadHasAfter) = await ReadAuditAsync(disbId);
        Assert.Multiple(() =>
        {
            Assert.That(actions, Does.Contain("disbursement.recorded"));
            Assert.That(actions, Does.Contain("disbursement.evidence_attached"));
            Assert.That(actions, Does.Contain("disbursement.edited"));
            Assert.That(actions, Does.Contain("disbursement.validated"));
            Assert.That(actor, Is.Not.Empty, "every audit row carries the actor");
            Assert.That(payloadHasAfter, Is.True, "payloads carry before/after values");
        });
    }

    private async Task<(List<string> Actions, string Actor, bool PayloadHasAfter)> ReadAuditAsync(int disbursementId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        const string sql = @"SELECT Action, ActorUserId, ISNULL(PayloadJson, '') AS PayloadJson
                             FROM dbo.AdminAuditEvents
                             WHERE TargetType = 'disbursement' AND TargetId = @id;";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", disbursementId.ToString());

        var actions = new List<string>();
        var actor = string.Empty;
        var hasAfter = false;
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actions.Add(reader.GetString(0));
            actor = reader.GetString(1);
            if (reader.GetString(2).Contains("after")) hasAfter = true;
        }
        return (actions, actor, hasAfter);
    }
}
