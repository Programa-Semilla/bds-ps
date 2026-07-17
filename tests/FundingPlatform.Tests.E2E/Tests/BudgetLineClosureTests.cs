using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 047 / US3 — the budget-line closure surface over real SQL: a happy close + reopen (stored
/// closure state round-trips), and a blocking leg (paid≠accepted). The full close-gate matrix is
/// covered by the ClosureGateTests integration suite; this proves the UI routes + persistence.
/// </summary>
[Category("BudgetLineClosure")]
public class BudgetLineClosureTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string Today = "2026-07-15";
    private string _pdf = string.Empty;
    private readonly List<string> _seeded = [];

    [SetUp]
    public void SetUp()
    {
        _pdf = Path.Combine(Path.GetTempPath(), $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_pdf, "%PDF-1.4\nclosure\n%%EOF\n"u8.ToArray());
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

    private async Task<(int appId, string finopEmail, string adminEmail)> SeedAsync()
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
        return (appId, finopEmail, adminEmail);
    }

    /// <summary>Edits the global-default rule to require no documents (so completeness passes trivially).</summary>
    private async Task RequireNoDocumentsAsync(string adminEmail)
    {
        await LoginAsync(Page, adminEmail, Pwd);
        await Page.GotoAsync($"{BaseUrl}/Admin/DocumentRules");
        await Page.Locator("[data-testid=docrule-row][data-category-id=global] [data-testid=docrule-edit]").ClickAsync();
        foreach (var type in new[] { "BankReceipt", "Invoice", "SignedAcceptance", "CreditNote", "RefundReceipt", "Other" })
        {
            await Page.Locator($"[data-testid=docrule-required][data-type={type}]").UncheckAsync();
        }
        await Page.Locator("[data-testid=docrule-save]").ClickAsync();
        await Expect(Page.Locator("[data-testid=docrule-list]")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    [Test]
    public async Task Close_ThenReopen_RoundTrips()
    {
        var (appId, finopEmail, adminEmail) = await SeedAsync();
        await RequireNoDocumentsAsync(adminEmail);

        // No required docs, no payments, no acceptance → paid==accepted==0 → the line closes cleanly.
        await LoginAsync(Page, finopEmail, Pwd);
        var page = new EvidencePage(Page);
        await page.GotoAsync(BaseUrl, appId);
        await Expect(page.CloseButton).ToBeVisibleAsync();

        await page.CloseButton.ClickAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.ClosedBadge).ToBeVisibleAsync();

        // Reopen with a reason → back to open (stored closure state round-trips on real SQL).
        await Page.Locator("[data-testid=line-reopen-reason]").First.FillAsync("revisión adicional");
        await page.ReopenButton.ClickAsync();
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(page.CloseButton).ToBeVisibleAsync(); // close available again
    }

    [Test]
    public async Task Close_AcceptanceWithoutMatchingPayment_Refused()
    {
        var (appId, finopEmail, adminEmail) = await SeedAsync();
        await RequireNoDocumentsAsync(adminEmail);

        await LoginAsync(Page, finopEmail, Pwd);
        var page = new EvidencePage(Page);
        await page.GotoAsync(BaseUrl, appId);

        // A signed acceptance (accepted = 100,000) with no matching validated payment (paid = 0)
        // → the paid==accepted leg blocks the close.
        await page.AttachAsync("SignedAcceptance", 100_000m, "ACT-1", Today, _pdf, 100_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();

        await page.CloseButton.ClickAsync();
        await Expect(page.ErrorToast).ToBeVisibleAsync();
        await Expect(page.ClosedBadge).ToHaveCountAsync(0); // still open
    }
}
