using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 046 / US2 — the Financial Operator commits/un-commits budget-lines on the disbursement
/// surface: the Committed dimension rises at line/tranche/participant levels, un-commit reverses,
/// and Auditor/Admin see the panel read-only (no commit controls, FR-021).
/// </summary>
[Category("BudgetLineCommit")]
public class BudgetLineCommitTests : AuthenticatedTestBase
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

    private async Task<(int appId, string operatorEmail, string adminEmail)> SeedExecutedAsync(string uid)
    {
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

        return (appId, operatorEmail, adminEmail);
    }

    [Test]
    public async Task Operator_CommitsAndUncommits_CommittedDimensionMoves()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var (appId, operatorEmail, _) = await SeedExecutedAsync(uid);
        await LoginAsync(Page, operatorEmail, Pwd);
        var page = new DisbursementPage(Page);
        await page.GotoAsync(BaseUrl, appId);

        // The single line sits under the synthetic "General" tranche, uncommitted (Committed = ₡0.00).
        var committedBadge = Page.Locator("[data-testid=line-committed]").First;
        var allocatedBadge = Page.Locator("[data-testid=line-allocated]").First;
        var lineStatus = Page.Locator("[data-testid=line-status]").First;
        var budgetText = (await allocatedBadge.InnerTextAsync()).Trim();
        Assert.That((await committedBadge.InnerTextAsync()).Trim(), Is.Not.EqualTo(budgetText));

        // Commit the line → Committed rises to the line budget at line + participant levels.
        await Page.Locator("[data-testid=line-commit-btn]").First.ClickAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=line-committed]").First).ToHaveTextAsync(budgetText);
        await Expect(Page.Locator("[data-testid=balance-committed]")).ToHaveTextAsync(budgetText);
        await Expect(lineStatus).ToContainTextAsync("Comprometida");

        // Un-commit → Committed falls back to zero.
        await Page.Locator("[data-testid=line-uncommit-btn]").First.ClickAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        Assert.That((await Page.Locator("[data-testid=line-committed]").First.InnerTextAsync()).Trim(),
            Is.Not.EqualTo(budgetText));
    }

    [Test]
    public async Task Admin_SeesPanelReadOnly_NoCommitControls()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var (appId, _, adminEmail) = await SeedExecutedAsync(uid);

        await LoginAsync(Page, adminEmail, Pwd);
        var page = new DisbursementPage(Page);
        await page.GotoAsync(BaseUrl, appId);

        // Admin can view the composed panel but has no write controls (FR-021 — money movement is
        // the operator's segregated duty).
        await Expect(Page.Locator("[data-testid=tranche-balance-panel]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=line-commit-btn]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=line-uncommit-btn]")).ToHaveCountAsync(0);
    }
}
