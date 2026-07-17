using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 047 / US2 — the admin required-document matrix + live per-line completeness over real SQL.
/// The global-default rule (Bank Receipt + Invoice + Signed Acceptance) drives a line's completeness;
/// attaching a document flips its type to present; editing the matrix re-drives completeness.
/// Exercises the DocumentRuleItem TINYINT round-trip + the UNIQUE(CategoryId) global-default row.
/// </summary>
[Category("RequiredDocMatrixCompleteness")]
public class RequiredDocMatrixCompletenessTests : AuthenticatedTestBase
{
    private const string Pwd = "Test123!";
    private const string Today = "2026-07-15";
    private string _pdf = string.Empty;
    private readonly List<string> _seeded = [];

    [SetUp]
    public void SetUp()
    {
        _pdf = Path.Combine(Path.GetTempPath(), $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(_pdf, "%PDF-1.4\ncompleteness\n%%EOF\n"u8.ToArray());
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

    /// <summary>Sets the shared global-default rule to require exactly <paramref name="requiredTypes"/>
    /// (self-contained precondition — the global default is shared mutable state across the fixture).</summary>
    private async Task SetGlobalRequiredAsync(string adminEmail, params string[] requiredTypes)
    {
        await LoginAsync(Page, adminEmail, Pwd);
        await Page.GotoAsync($"{BaseUrl}/Admin/DocumentRules");
        await Page.Locator("[data-testid=docrule-row][data-category-id=global] [data-testid=docrule-edit]").ClickAsync();
        await Expect(Page.Locator("[data-testid=docrule-edit-form]")).ToBeVisibleAsync();
        foreach (var type in new[] { "BankReceipt", "Invoice", "SignedAcceptance", "CreditNote", "RefundReceipt", "Other" })
        {
            var box = Page.Locator($"[data-testid=docrule-required][data-type={type}]");
            if (requiredTypes.Contains(type)) { await box.CheckAsync(); } else { await box.UncheckAsync(); }
        }
        await Page.Locator("[data-testid=docrule-save]").ClickAsync();
        await Expect(Page.Locator("[data-testid=docrule-list]")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    [Test]
    public async Task Completeness_ReflectsGlobalDefault_AndMatrixEdits()
    {
        var (appId, finopEmail, adminEmail) = await SeedAsync();

        // (0) Establish the precondition: the global default requires 3 docs (self-contained — the row
        // is shared mutable state; other tests may have changed it).
        await SetGlobalRequiredAsync(adminEmail, "BankReceipt", "Invoice", "SignedAcceptance");

        // (1) As Financial Operator: 3 docs required, none present → the line is incomplete.
        await LoginAsync(Page, finopEmail, Pwd);
        var page = new EvidencePage(Page);
        await page.GotoAsync(BaseUrl, appId);
        await Expect(page.CompletenessMatrix).ToBeVisibleAsync();
        await Expect(page.IncompleteBadge).ToBeVisibleAsync();
        // Invoice is required but not present.
        await Expect(Page.Locator("[data-testid=completeness-type][data-type=Invoice][data-present=false]"))
            .ToBeVisibleAsync();

        // (2) Attach an Invoice → its type flips to present in the completeness matrix.
        await page.AttachAsync("Invoice", 400_000m, "F-001", Today, _pdf, 400_000m);
        await Expect(page.SuccessToast).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=completeness-type][data-type=Invoice][data-present=true]"))
            .ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // (3) As Admin: edit the global-default rule to require ONLY Invoice (exercises the TINYINT
        // round-trip + UNIQUE(CategoryId) single-NULL global-default row).
        await SetGlobalRequiredAsync(adminEmail, "Invoice");

        // (4) As Financial Operator again: the line now requires only Invoice (present) → complete.
        await LoginAsync(Page, finopEmail, Pwd);
        await page.GotoAsync(BaseUrl, appId);
        await Expect(Page.Locator("[data-testid=completeness-complete-badge]").First).ToBeVisibleAsync();
    }
}
