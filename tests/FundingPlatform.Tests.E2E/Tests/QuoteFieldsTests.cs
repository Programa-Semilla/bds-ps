using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 039 / US2 — delivery lead time and warranty are required on every quotation
/// (FR-001/FR-002/FR-003/SC-004). A blank/zero value is rejected with an es-CR
/// message; valid values save.
/// </summary>
public class QuoteFieldsTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"qf-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\nquote\n%%EOF\n");
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    [Test]
    public async Task AddQuote_ZeroDeliveryLeadTime_RejectedWithEsCrMessage()
    {
        var (appId, supplierPage) = await StartAddSupplierAsync();

        // Everything valid except delivery (= 0) → rejected; warranty stays valid.
        await supplierPage.FillSupplierFormAsync(
            $"QFZERO-{_uniqueId}", "Proveedor Cero Entrega", price: 500m, validUntil: "2027-12-31",
            filePath: _testFilePath, deliveryLeadTimeDays: 0, warrantyMonths: 12);
        await supplierPage.SubmitAsync();

        // Stays on the add form with the es-CR validation message; no redirect.
        await Expect(Page.GetByText("El tiempo de entrega debe ser mayor a cero.")).ToBeVisibleAsync();
        await Expect(Page).Not.ToHaveURLAsync(new Regex($@"/Application/Edit/{appId}$"));
    }

    [Test]
    public async Task AddQuote_ZeroWarranty_RejectedWithEsCrMessage()
    {
        var (appId, supplierPage) = await StartAddSupplierAsync();

        // Everything valid except warranty (= 0) → rejected; delivery stays valid.
        await supplierPage.FillSupplierFormAsync(
            $"QFWZERO-{_uniqueId}", "Proveedor Cero Garantía", price: 500m, validUntil: "2027-12-31",
            filePath: _testFilePath, deliveryLeadTimeDays: 15, warrantyMonths: 0);
        await supplierPage.SubmitAsync();

        await Expect(Page.GetByText("La garantía debe ser mayor a cero.")).ToBeVisibleAsync();
        await Expect(Page).Not.ToHaveURLAsync(new Regex($@"/Application/Edit/{appId}$"));
    }

    [Test]
    public async Task AddQuote_ValidDeliveryAndWarranty_Saves()
    {
        var (appId, supplierPage) = await StartAddSupplierAsync();

        await supplierPage.FillSupplierFormAsync(
            $"QFOK-{_uniqueId}", "Proveedor Válido", price: 500m, validUntil: "2027-12-31",
            filePath: _testFilePath, deliveryLeadTimeDays: 15, warrantyMonths: 18);
        await supplierPage.SubmitAsync();

        // Saved → redirect back to the draft editor with the success toast.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    private async Task<(int appId, SupplierPage supplierPage)> StartAddSupplierAsync()
    {
        var email = $"qf_app_{_uniqueId}@example.com";
        const string password = "Test123!";
        await RegisterUserAsync(Page, email, password, "Quote", "Fields", $"QF-{_uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Quote Fields Item", 0, "Specs", BaseUrl);

        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        return (appId, new SupplierPage(Page));
    }
}
