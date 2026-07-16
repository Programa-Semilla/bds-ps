using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 046 / US4 — filtering budget-lines on the disbursement surface narrows the list (SC-005).
/// </summary>
[Category("BudgetLineFilter")]
public class BudgetLineFilterTests : AuthenticatedTestBase
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

    [Test]
    public async Task StatusFilter_NarrowsBudgetLineList()
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

        // Commit the single line → it becomes "Committed".
        await Page.Locator("[data-testid=line-commit-btn]").First.ClickAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // Filter to Committed → the line is listed.
        await Page.Locator("[data-testid=filter-status]").SelectOptionAsync("Committed");
        await Page.Locator("[data-testid=filter-apply]").ClickAsync();
        await Expect(Page.Locator("[data-testid=budget-line-row]")).ToHaveCountAsync(1);

        // Filter to Uncommitted → the (now-committed) line drops out; the panel is empty.
        await Page.Locator("[data-testid=filter-status]").SelectOptionAsync("Uncommitted");
        await Page.Locator("[data-testid=filter-apply]").ClickAsync();
        await Expect(Page.Locator("[data-testid=budget-line-row]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=budget-line-empty]")).ToBeVisibleAsync();
    }
}
