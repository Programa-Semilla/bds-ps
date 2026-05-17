using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Suppliers;

/// <summary>
/// Spec 013 supplier-catalog E2E (US1-US7).
///
/// Each test exercises one user-story acceptance scenario end-to-end against
/// the real Aspire+SQL fixture so the schema migration, EF mappings, controllers,
/// and Razor partials are all live.
/// </summary>
public class SupplierCatalogTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _testFilePath = Path.Combine(Path.GetTempPath(), $"sc-{_uniqueId}.pdf");
        File.WriteAllText(_testFilePath, "Test PDF content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    // ============================== US3: Create a Brand-New Supplier in Draft

    [Test]
    public async Task US3_NewLegalId_LandsOnNewSupplierForm_AndCreatesDraft()
    {
        var (appId, _) = await SetupApplicantWithItemAsync("us3");

        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();

        var supplier = new SupplierPage(Page);
        var outcome = await supplier.SearchByLegalIdAsync($"3-101-{_uniqueId.ToUpper()}");
        Assert.That(outcome, Is.EqualTo("Empty"),
            "A brand-new legal ID must land on the new-supplier form.");

        await supplier.FillNewSupplierFormAsync(
            name: "Test Co", branchName: "Sede principal", contact: "Ana", email: "ana@x.com");
        await supplier.FillQuotationFieldsAsync(1500m, "2027-12-31", _testFilePath);
        await supplier.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    [Test]
    public async Task US3_NewSupplierForm_DoesNotShowComplianceCheckboxes()
    {
        var (appId, _) = await SetupApplicantWithItemAsync("us3-noflags");

        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        var supplier = new SupplierPage(Page);
        await supplier.SearchByLegalIdAsync($"3-101-{_uniqueId.ToUpper()}");

        // Spec 013 SC-002: zero compliance checkboxes on applicant-facing forms.
        var ccssBox = Page.Locator("input[type=checkbox][name=IsCompliantCCSS]");
        var einvoiceBox = Page.Locator("input[type=checkbox][name=HasElectronicInvoice]");
        await Expect(ccssBox).ToHaveCountAsync(0);
        await Expect(einvoiceBox).ToHaveCountAsync(0);
    }

    // ============================== US1: Reuse a Verified Supplier

    [Test]
    public async Task US1_ExistingDraftSupplier_VisibleToCreator_BranchPickerAppears()
    {
        // Same applicant: create a Draft supplier, then come back and search it.
        var (appId, _) = await SetupApplicantWithItemAsync("us1");
        var legalId = $"3-101-{_uniqueId.ToUpper()}";

        // Create item 1 supplier
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        var supplier = new SupplierPage(Page);
        await supplier.SearchByLegalIdAsync(legalId);
        await supplier.FillNewSupplierFormAsync(name: "Reuse Co", branchName: "Sede principal");
        await supplier.FillQuotationFieldsAsync(1000m, "2027-12-31", _testFilePath);
        await supplier.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Add a 2nd item, search the same legal ID — it should be a Hit (Draft visible to creator).
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Second Item", 0, "Second specs", BaseUrl);
        await Page.Locator("a:has-text('Agregar proveedor')").Last.ClickAsync();
        var outcome = await supplier.SearchByLegalIdAsync(legalId);
        Assert.That(outcome, Is.EqualTo("Hit"),
            "Creator must see their own Draft supplier in lookup (FR-003).");
        await Expect(supplier.BranchPicker).ToBeVisibleAsync();
    }

    // ============================== US7: Admin queue defaults to PendingReview

    [Test]
    public async Task US7_AdminSuppliersPage_DefaultsToPendingReviewFilter()
    {
        await EnsureSeededAdminLoginAsync();

        var page = new AdminSuppliersListPage(Page);
        await page.GoToAsync(BaseUrl);

        await Expect(page.StatusFilter).ToBeVisibleAsync();
        // Default status filter is PendingReview (value "1").
        var selected = await page.StatusFilter.EvaluateAsync<string>("el => el.value");
        Assert.That(selected, Is.EqualTo("1"),
            "Admin Suppliers page must default to PendingReview filter (FR-030).");
    }

    // -----------------------------------------------------------------------

    private async Task<(int appId, int itemId)> SetupApplicantWithItemAsync(string scenarioPrefix)
    {
        var email = $"{scenarioPrefix}_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, email, password,
            "Test", "Applicant", $"L-{scenarioPrefix}-{_uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, $"Item {scenarioPrefix}", 0, "Specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        return (appId, 0); // itemId not needed for happy-path navigation; tests use the link.
    }

    private async Task EnsureSeededAdminLoginAsync()
    {
        // Register a fresh admin per test to avoid cross-test pollution.
        var adminEmail = $"sc_admin_{_uniqueId}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, adminEmail, password, "Test", "Admin", $"SCAD-{_uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, password);
    }
}
