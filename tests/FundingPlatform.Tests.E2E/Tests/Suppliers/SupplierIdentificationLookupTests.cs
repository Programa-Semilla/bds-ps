using System.Linq;
using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Suppliers;

/// <summary>
/// Spec 026 / US2 — supplier identification is type-aware (Cédula jurídica / NITE)
/// and the lookup is hyphenation-tolerant: a known supplier resolves whether the
/// query is typed with hyphens or as bare digits. A new NITE supplier persists and
/// is found on a later lookup.
/// </summary>
public class SupplierIdentificationLookupTests : AuthenticatedTestBase
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

    private async Task<int> CreateDraftWithItemAsync(string uniqueId)
    {
        var email = $"sup_lookup_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Sup", "Lookup", $"SL-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Equipo", 0, "Specs", BaseUrl);
        return appId;
    }

    private async Task OpenAddSupplierAsync(int appId)
    {
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var link = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await Expect(link).ToBeVisibleAsync();
        await link.ClickAsync();
    }

    [Test]
    public async Task SupplierLookup_MatchesRegardlessOfHyphenation()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var juridica = IdentificationData.CedulaJuridica($"LOOKUP-{uniqueId}"); // 3-XXX-XXXXXX
        var bareDigits = new string(juridica.Where(char.IsDigit).ToArray());     // 3XXXXXXXXX

        var appId = await CreateDraftWithItemAsync(uniqueId);
        var supplierPage = new SupplierPage(Page);

        // Create a Draft supplier with the canonical jurídica id (visible to its creator).
        await OpenAddSupplierAsync(appId);
        await supplierPage.FillSupplierFormAsync(juridica, "Proveedora Lookup", 900m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Hyphenated query → Hit.
        await OpenAddSupplierAsync(appId);
        Assert.That(await supplierPage.SearchByLegalIdAsync(juridica), Is.EqualTo("Hit"),
            "Hyphenated query should resolve to the existing supplier.");

        // Bare-digit query → same Hit.
        await OpenAddSupplierAsync(appId);
        Assert.That(await supplierPage.SearchByLegalIdAsync(bareDigits), Is.EqualTo("Hit"),
            "Bare-digit query should resolve to the same supplier (hyphenation-tolerant lookup).");
    }

    [Test]
    public async Task NewNiteSupplier_PersistsAndIsFound()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var nite = IdentificationData.Nite($"NITE-{uniqueId}");

        var appId = await CreateDraftWithItemAsync(uniqueId);
        var supplierPage = new SupplierPage(Page);

        await OpenAddSupplierAsync(appId);
        var outcome = await supplierPage.SearchByLegalIdAsync(nite);
        Assert.That(outcome, Is.EqualTo("Empty"), "A brand-new NITE should land on the new-supplier form.");

        await supplierPage.SelectSupplierTypeAsync("Nite");
        await supplierPage.FillNewSupplierFormAsync("Proveedora NITE", "Sede principal");
        await supplierPage.FillQuotationFieldsAsync(950m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Re-open and confirm the NITE supplier persisted and is found.
        await OpenAddSupplierAsync(appId);
        Assert.That(await supplierPage.SearchByLegalIdAsync(nite), Is.EqualTo("Hit"),
            "The newly-created NITE supplier should be found on a later lookup.");
    }
}
