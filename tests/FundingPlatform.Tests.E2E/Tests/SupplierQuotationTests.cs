using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 013 rewrite: the supplier-add flow no longer accepts compliance flags
/// from the applicant. Tests exercise the new branch-aware step flow:
///   - search by legal ID (debounced)
///   - if no hit, fill the new-supplier form (Draft)
///   - if hit, pick a branch (or add a new one)
///   - quotation fields are always required
/// </summary>
public class SupplierQuotationTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test-quotation-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Test quotation document content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Test]
    public async Task AddSupplier_NewLegalId_CreatesDraftAndAttachesQuotation()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"supplier_test_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Supplier", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Server Equipment", 0, "High-performance server", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await Expect(addSupplierLink).ToBeVisibleAsync();
        await addSupplierLink.ClickAsync();

        var supplierPage = new SupplierPage(Page);
        var outcome = await supplierPage.SearchByLegalIdAsync($"SUP-{uniqueId}");
        Assert.That(outcome, Is.EqualTo("Empty"), "A brand-new legal ID should land on the new-supplier form.");

        await supplierPage.FillNewSupplierFormAsync(
            name: "Test Supplier Corp",
            branchName: "Sede principal",
            contact: "John Doe",
            email: "supplier@test.com",
            phone: "555-0100",
            province: "San Jose");

        await supplierPage.FillQuotationFieldsAsync(
            price: 1500.00m, validUntil: "2027-12-31", filePath: _testFilePath, currency: "USD");
        await supplierPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var itemRow = Page.Locator("table tbody tr:has-text('Server Equipment')");
        await Expect(itemRow).ToBeVisibleAsync();
    }

    [Test]
    public async Task AddSupplier_DuplicateLegalId_ReusesExistingSupplier()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"dup_supplier_{uniqueId}@example.com";
        var password = "Test123!";
        var supplierLegalId = $"SUP-DUP-{uniqueId}";

        await RegisterUserAsync(Page, email, password, "Dup", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Network Switch", 0, "48-port managed switch", BaseUrl);

        // First save: create a Draft supplier.
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await Expect(addSupplierLink).ToBeVisibleAsync();
        await addSupplierLink.ClickAsync();

        var supplierPage = new SupplierPage(Page);
        await supplierPage.SearchByLegalIdAsync(supplierLegalId);
        await supplierPage.FillNewSupplierFormAsync(
            name: "Duplicate Supplier", branchName: "Sede principal");
        await supplierPage.FillQuotationFieldsAsync(2000.00m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Second save (same item): the (item, supplier) UNIQUE constraint
        // should reject a duplicate quotation against the same supplier.
        var addSupplierLink2 = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink2.ClickAsync();
        var outcome = await supplierPage.SearchByLegalIdAsync(supplierLegalId);
        Assert.That(outcome, Is.EqualTo("Hit"), "The Draft supplier the same applicant created should be visible to them.");

        // Pick the default branch the supplier already has, fill quotation, submit.
        await supplierPage.SelectFirstBranchAsync();
        await supplierPage.FillQuotationFieldsAsync(2500.00m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        // Should land back on the same form with a duplicate-supplier error.
        var errorMessage = Page.Locator(".text-danger li, .alert-danger");
        await Expect(errorMessage.First).ToBeVisibleAsync();
    }
}
