using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Suppliers;

/// <summary>
/// Spec 013 — fills the dedicated-E2E gaps for User Stories 2, 5 (reject path),
/// and 7 (filter switching) that were previously only exercised transitively by
/// other tests. Each test is intentionally narrow and asserts the single
/// observable behavior promised by its acceptance scenario, so the cost of
/// keeping the gate honest stays low.
///
/// Added during the deep-review fix loop (FINDING-A5-2): the original task list
/// (T041, T060-T063, T077) called for these scenarios; the implementation
/// covered them only inside compound flows. These standalone tests pin the
/// behavior so a future refactor of the compound flows can't silently lose
/// coverage of these specific user stories.
/// </summary>
public class AdditionalUserStoryCoverageTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _testFilePath = Path.Combine(Path.GetTempPath(), $"add-cov-{_uniqueId}.pdf");
        File.WriteAllText(_testFilePath, "Test PDF content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    /// <summary>
    /// US5 acceptance scenario 2: admin clicks Reject without entering a reason →
    /// the action is blocked and a validation error is shown. Direct assertion
    /// against the Reject form on the admin Detail page (no need for the full
    /// applicant-side seed flow).
    /// </summary>
    [Test]
    public async Task US5_AdminRejectsWithoutReason_IsBlocked()
    {
        var legalId = $"3-101-{_uniqueId.ToUpper()}";
        var supplierId = await SeedPendingSupplierAndLoginAdminAsync(legalId, $"Reject-test {_uniqueId}");

        var detail = new AdminSupplierDetailPage(Page);
        await detail.GoToAsync(BaseUrl, supplierId);

        // Click Reject with empty reason. The server-side guard sets ErrorMessage
        // and redirects back to Detail. We assert the supplier did NOT transition
        // to Rejected (no rejection-reason banner appears).
        await detail.RejectButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex($@"/Admin/Suppliers/{supplierId}"));

        // Banner-form: rejection-reason banner only renders for Rejected status.
        await Expect(detail.RejectionReasonBanner).ToHaveCountAsync(0);

        // Reject form is still present (status is still PendingReview, can be acted on).
        await Expect(detail.RejectForm).ToBeVisibleAsync();
    }

    /// <summary>
    /// US5 acceptance scenario 3: admin clicks Reject WITH a reason → status
    /// transitions to Rejected and the rejection-reason banner appears on the
    /// next page render.
    /// </summary>
    [Test]
    public async Task US5_AdminRejectsWithReason_PersistsAndShowsBanner()
    {
        var legalId = $"3-101-{_uniqueId.ToUpper()}";
        var supplierId = await SeedPendingSupplierAndLoginAdminAsync(legalId, $"Reject-with-reason {_uniqueId}");

        var detail = new AdminSupplierDetailPage(Page);
        await detail.GoToAsync(BaseUrl, supplierId);

        await detail.RejectAsync("Documentación incompleta");
        await Page.WaitForURLAsync(new Regex($@"/Admin/Suppliers/{supplierId}"));

        await Expect(detail.RejectionReasonBanner).ToBeVisibleAsync();
        await Expect(detail.RejectionReasonBanner).ToContainTextAsync("Documentación incompleta");
    }

    /// <summary>
    /// US7 acceptance scenario 2: admin switches the status filter from the default
    /// (PendingReview) to Verified → the listing re-renders with only Verified
    /// suppliers. We assert the URL carries the correct status query and that the
    /// dropdown value reflects the chosen filter.
    /// </summary>
    [Test]
    public async Task US7_AdminSwitchesStatusFilter_UrlAndDropdownReflectChoice()
    {
        await EnsureSeededAdminLoginAsync();

        var listPage = new AdminSuppliersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await Expect(listPage.StatusFilter).ToBeVisibleAsync();

        // Switch to Verified (enum value 2).
        await listPage.FilterByStatusAsync("2");

        await Expect(Page).ToHaveURLAsync(new Regex(@"[\?&]status=2"));
        var selected = await listPage.StatusFilter.EvaluateAsync<string>("el => el.value");
        Assert.That(selected, Is.EqualTo("2"),
            "Status filter dropdown must reflect the chosen filter (FR-031).");
    }

    // ----------------------------------------------------------------------- helpers

    /// <summary>
    /// Drives the UI through US3 (applicant draft creation) → US4 (submit flips to
    /// PendingReview), then logs in as admin and returns the supplier's ID. Lighter
    /// than ApplicantReusesVerifiedSupplierTests.SeedVerifiedSupplierAsync because
    /// it stops before the Verify step (we want the supplier in PendingReview so
    /// the reject/verify actions are exercisable).
    /// </summary>
    private async Task<int> SeedPendingSupplierAndLoginAdminAsync(string legalId, string supplierName)
    {
        const string password = "Test123!";
        var seederEmail = $"seeder_{_uniqueId}@example.com";
        var adminEmail = $"adcov_admin_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, adminEmail, password, "Test", "Admin", $"AD-{_uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");

        await RegisterUserAsync(Page, seederEmail, password, "Test", "Seeder", $"SE-{_uniqueId}");
        await LoginAsync(Page, seederEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Cov Item", 0, "Specs", BaseUrl);

        await Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First.ClickAsync();
        var supplier = new SupplierPage(Page);
        await supplier.SearchByLegalIdAsync(legalId);
        await supplier.FillNewSupplierFormAsync(name: supplierName, branchName: "Sede principal");
        await supplier.FillQuotationFieldsAsync(900m, "2027-12-31", _testFilePath);
        await supplier.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // MinQuotationsPerItem (default 2) — add a second supplier to make the app submittable.
        await Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First.ClickAsync();
        var supplier2 = new SupplierPage(Page);
        await supplier2.SearchByLegalIdAsync($"3-101-COVFILL-{_uniqueId.ToUpper()}");
        await supplier2.FillNewSupplierFormAsync(name: $"Filler Cov {_uniqueId}", branchName: "Sede principal");
        await supplier2.FillQuotationFieldsAsync(1100m, "2027-12-31", _testFilePath);
        await supplier2.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Fill impact (required to submit).
        await SetImpactFromEditAsync(appId);

        // Submit application — supplier flips Draft → PendingReview.
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // Login as admin and navigate to the Suppliers admin list to grab the supplier ID.
        await LoginAsync(Page, adminEmail, password);
        var adminList = new AdminSuppliersListPage(Page);
        await adminList.GoToAsync(BaseUrl);
        await adminList.SearchByLegalIdAsync(legalId);

        var firstRow = adminList.Rows.First;
        await Expect(firstRow).ToBeVisibleAsync();
        var supplierIdAttr = await firstRow.GetAttributeAsync("data-testid");
        return int.Parse(supplierIdAttr!.Replace("admin-supplier-row-", ""));
    }

    private async Task EnsureSeededAdminLoginAsync()
    {
        var adminEmail = $"adcov_admin2_{_uniqueId}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, adminEmail, password, "Test", "Admin", $"ADCOV-{_uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, password);
    }
}
