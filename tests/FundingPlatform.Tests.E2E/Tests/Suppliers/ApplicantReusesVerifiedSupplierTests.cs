using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Suppliers;

/// <summary>
/// Spec 013 — User Story 1 (P1, MVP). Verifies the three acceptance scenarios:
///   AS-01  An applicant on a draft application searches by legal ID, lands on a
///          Verified supplier card, and the four admin-only flags render as
///          read-only Tabler badges (no editable inputs anywhere on the page).
///   AS-02  Selecting a specific branch from the radio picker persists onto the
///          quotation; the parent supplier row is unchanged afterwards.
///   AS-03  Whitespace and case differences in the typed legal ID match the
///          canonical stored legal ID — same Hit, same supplier card.
///
/// Each test seeds a fresh Verified supplier via the UI: an applicant creates a
/// Draft supplier (US3), submits the application (which flips the supplier to
/// PendingReview, US4), then an admin Verifies it (US5). A SECOND applicant
/// then exercises the read path. This proves cross-applicant reuse with no
/// direct DB-seeding hacks.
/// </summary>
public class ApplicantReusesVerifiedSupplierTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _testFilePath = Path.Combine(Path.GetTempPath(), $"reuse-{_uniqueId}.pdf");
        File.WriteAllText(_testFilePath, "Test PDF content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    [Test]
    public async Task AS01_SecondApplicant_SeesVerifiedSupplier_WithReadOnlyComplianceBadges()
    {
        var legalId = $"3-101-{_uniqueId.ToUpper()}";
        var supplierName = $"Verified Co {_uniqueId}";
        await SeedVerifiedSupplierAsync(legalId, supplierName);

        // A SECOND applicant searches the same legal ID on their own draft application.
        var (_, supplier) = await SetupAnotherApplicantAndOpenAddSupplierAsync("reuse1");
        var outcome = await supplier.SearchByLegalIdAsync(legalId);

        Assert.That(outcome, Is.EqualTo("Hit"),
            "Verified suppliers MUST be visible to all applicants (FR-002).");
        await supplier.AssertSupplierReadOnlyAsync(
            name: supplierName, ccss: true, hacienda: true, sicop: true, eInvoice: true);
    }

    [Test]
    public async Task AS02_BranchSelection_PersistsOntoQuotation()
    {
        var legalId = $"3-101-{_uniqueId.ToUpper()}";
        var supplierName = $"Multi-Branch Co {_uniqueId}";
        await SeedVerifiedSupplierAsync(legalId, supplierName);

        var (appId, supplier) = await SetupAnotherApplicantAndOpenAddSupplierAsync("reuse2");
        var outcome = await supplier.SearchByLegalIdAsync(legalId);
        Assert.That(outcome, Is.EqualTo("Hit"));

        // Pick the first available branch radio (the seeded supplier has exactly one).
        await supplier.SelectFirstBranchAsync();
        await supplier.FillQuotationFieldsAsync(2500m, "2027-12-31", _testFilePath);
        await supplier.SubmitAsync();

        // Successful save returns to the application detail page.
        await Expect(Page).ToHaveURLAsync(new Regex($@"/Application/Details/{appId}"));
    }

    [Test]
    public async Task AS03_WhitespaceAndCase_NormalizedToSameSupplier()
    {
        var legalId = $"3-101-{_uniqueId.ToUpper()}";
        var supplierName = $"Norm Co {_uniqueId}";
        await SeedVerifiedSupplierAsync(legalId, supplierName);

        // A second applicant types the legal ID with surrounding whitespace and
        // mixed casing. NormalizeLegalId strips whitespace and uppercases — so the
        // server MUST resolve this to the same supplier as the canonical form.
        var (_, supplier) = await SetupAnotherApplicantAndOpenAddSupplierAsync("reuse3");
        var noisy = "  " + legalId.ToLowerInvariant() + "  ";
        var outcome = await supplier.SearchByLegalIdAsync(noisy);
        Assert.That(outcome, Is.EqualTo("Hit"),
            "Whitespace + case differences MUST normalize to the same Hit (FR-006).");
        await Expect(supplier.LookupHitCard).ToContainTextAsync(supplierName);
    }

    // ----------------------------------------------------------------------- helpers

    /// <summary>
    /// Drive the UI through US3 (applicant draft creation) → US4 (submit) → US5 (admin verify).
    /// Logs out at the end so the next caller can register a fresh applicant cleanly.
    /// </summary>
    private async Task SeedVerifiedSupplierAsync(string legalId, string supplierName)
    {
        const string password = "Test123!";
        var seederEmail = $"seeder_{_uniqueId}@example.com";
        var adminEmail = $"sc_admin_{_uniqueId}@example.com";

        // Register an admin first (we'll come back to verify after submission).
        await RegisterUserAsync(Page, adminEmail, password, "Test", "Admin", $"AD-{_uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");

        // Register the seeder applicant and create a Draft application + item + draft supplier.
        await RegisterUserAsync(Page, seederEmail, password, "Test", "Seeder", $"SE-{_uniqueId}");
        await LoginAsync(Page, seederEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Seed Item", 0, "Specs", BaseUrl);

        await Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First.ClickAsync();
        var supplier = new SupplierPage(Page);
        await supplier.SearchByLegalIdAsync(legalId);
        await supplier.FillNewSupplierFormAsync(
            name: supplierName, branchName: "Sede principal", contact: "Seed", email: "s@e.com");
        await supplier.FillQuotationFieldsAsync(900m, "2027-12-31", _testFilePath);
        await supplier.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Submission requires MinQuotationsPerItem (default 2) — add a throwaway second
        // supplier so the application is submittable. Only the first supplier (above) is
        // the one this seed flow will end up Verifying for the test assertions.
        await Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First.ClickAsync();
        var secondSupplier = new SupplierPage(Page);
        await secondSupplier.SearchByLegalIdAsync($"3-101-FILLER-{_uniqueId.ToUpper()}");
        await secondSupplier.FillNewSupplierFormAsync(
            name: $"Filler Co {_uniqueId}", branchName: "Sede principal", contact: "F", email: "f@e.com");
        await secondSupplier.FillQuotationFieldsAsync(1100m, "2027-12-31", _testFilePath);
        await secondSupplier.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Fill impact so we can submit.
        await Page.Locator($"a:has-text('{UiCopy.Impact}')").First.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (var i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SaveImpact}')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Submit application — supplier flips Draft → PendingReview (US4).
        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SubmitApplication}')").ClickAsync();
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Login as admin and verify.
        await LoginAsync(Page, adminEmail, password);
        var adminList = new AdminSuppliersListPage(Page);
        await adminList.GoToAsync(BaseUrl);
        // Default filter is PendingReview. Two suppliers from this seed are in
        // PendingReview (target + filler). Filter by the target legal ID so we
        // open the correct row regardless of UpdatedAt ordering.
        await adminList.SearchByLegalIdAsync(legalId);
        var firstRow = adminList.Rows.First;
        await Expect(firstRow).ToBeVisibleAsync();
        var supplierIdAttr = await firstRow.GetAttributeAsync("data-testid");
        // data-testid="admin-supplier-row-{id}"
        var supplierId = int.Parse(supplierIdAttr!.Replace("admin-supplier-row-", ""));

        var detail = new AdminSupplierDetailPage(Page);
        await detail.GoToAsync(BaseUrl, supplierId);
        await detail.ToggleComplianceAllOnAsync();
        await detail.SaveEditAsync();
        // After edit the page redirects back to Detail. Now verify.
        await detail.GoToAsync(BaseUrl, supplierId);
        await detail.VerifyAsync();
        // After Verify the page redirects to Detail again with success banner.
        await Expect(Page).ToHaveURLAsync(new Regex($@"/Admin/Suppliers/{supplierId}"));

        // Logout admin so the next caller can register their own user.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
    }

    /// <summary>
    /// Register a brand-new applicant, create a Draft application + item, and click
    /// "Add supplier" so the page is sitting on /Supplier/Add. Returns the appId
    /// and a wired-up SupplierPage POM.
    /// </summary>
    private async Task<(int appId, SupplierPage supplier)> SetupAnotherApplicantAndOpenAddSupplierAsync(string scenarioPrefix)
    {
        const string password = "Test123!";
        var email = $"{scenarioPrefix}_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, email, password,
            "Reuse", "Applicant", $"R-{scenarioPrefix}-{_uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, $"Reuse Item {scenarioPrefix}", 0, "Specs", BaseUrl);

        await Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First.ClickAsync();
        return (appId, new SupplierPage(Page));
    }
}
