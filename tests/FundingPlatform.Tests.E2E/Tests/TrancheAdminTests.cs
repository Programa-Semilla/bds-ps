using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 046 / US1 — the reviewer subdivides the allocation into tranches on the pre-audit review
/// surface: create a tranche, assign a line, derived amount = Σ its lines' budgets, Σ tranches =
/// the allocation, an unassigned line falls into the synthetic "General" tranche, and after the
/// agreement executes the tranche structure is frozen (the editor is gone).
/// </summary>
[Category("TrancheAdmin")]
public class TrancheAdminTests : AuthenticatedTestBase
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
    public async Task Reviewer_DefinesTranche_AssignsLine_DerivesAmount_FreezesAtExecution()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var qPath = Path.Combine(Path.GetTempPath(), $"q-{uid}.pdf");
        File.WriteAllText(qPath, "Quotation placeholder");
        _seeded.Add(qPath);

        var (appId, applicantEmail, _) = await CreateApplicationAndSubmitResponseAsync(uid, qPath);
        var reviewerEmail = $"seed_reviewer_{uid}@example.com";
        var adminEmail = $"seed_admin_{uid}@example.com";

        await LoginAsync(Page, reviewerEmail, Pwd);
        var page = new TranchePage(Page);
        await page.GotoReviewAsync(BaseUrl, appId);

        // Editor visible pre-audit; every line is unassigned → synthetic "General" present with the
        // full allocation.
        await Expect(page.Editor).ToBeVisibleAsync();
        await Expect(page.Synthetic).ToBeVisibleAsync();
        var allocationText = (await page.AllocationTotal.InnerTextAsync()).Trim();
        var syntheticText = (await page.SyntheticAmount.InnerTextAsync()).Trim();
        Assert.That(syntheticText, Is.EqualTo(allocationText), "unassigned line's synthetic amount = the allocation");

        // Create a tranche and assign the single line to it.
        var itemId = await page.FirstLineItemIdAsync();
        await page.CreateTrancheAsync("Tramo 1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await page.AssignLineToTrancheByLabelAsync(itemId, "Tramo 1");
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        // Synthetic gone (nothing unassigned); the tranche's derived amount = the allocation.
        await Expect(page.Synthetic).ToHaveCountAsync(0);
        var trancheAmount = (await page.FirstTrancheDerivedAmount.InnerTextAsync()).Trim();
        Assert.That(trancheAmount, Is.EqualTo(allocationText), "Σ tranche = allocation to the colón");

        // Execute the agreement → the tranche structure is frozen (the editor is no longer rendered).
        _seeded.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, adminEmail, applicantEmail, reviewerEmail, CreateBlobServiceClient()));

        await page.GotoReviewAsync(BaseUrl, appId);
        await Expect(page.Editor).ToHaveCountAsync(0);
    }
}
