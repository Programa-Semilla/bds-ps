using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 035 / US3 / T049 — quotation reuse within an application: reuse a sibling
/// line item's supplier + uploaded document with this line's own price; the reuse
/// list is scoped to the application; a multi-product vendor quote becomes one reused
/// line item per product (N-product → N line items, SC-002).
/// </summary>
public class QuotationReuseTests : AuthenticatedTestBase
{
    private string _quotationFile = string.Empty;

    [SetUp]
    public void SetUpQuotationFile()
    {
        _quotationFile = Path.Combine(Path.GetTempPath(), $"reuse-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFile, "%PDF-1.4 reuse placeholder");
    }

    [TearDown]
    public void DeleteQuotationFile()
    {
        if (File.Exists(_quotationFile)) File.Delete(_quotationFile);
    }

    private async Task<int> CreateDraftAsync(string prefix)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"{prefix}_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Reuse", "Tester", $"RID-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        return int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
    }

    /// <summary>Collects the line-item ids (in row order) from the draft editor.</summary>
    private async Task<List<int>> GetItemIdsAsync(int appId)
    {
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var links = Page.Locator("[data-testid=application-edit-item-row] a:has-text('Editar')");
        var n = await links.CountAsync();
        var ids = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var href = await links.Nth(i).GetAttributeAsync("href") ?? string.Empty;
            var m = Regex.Match(href, @"/Item/(\d+)/Edit");
            if (m.Success) ids.Add(int.Parse(m.Groups[1].Value));
        }
        return ids;
    }

    [Test]
    public async Task Reuse_CreatesIndependentQuotation_FromSiblingSupplierAndDocument()
    {
        var appId = await CreateDraftAsync("reuse_basic");
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Línea A", 0, "Specs", BaseUrl);
        await itemPage.AddItemAsync(appId, "Línea B", 0, "Specs", BaseUrl);
        var ids = await GetItemIdsAsync(appId);
        var (itemA, itemB) = (ids[0], ids[1]);

        // Quote item A with a new supplier (price 900, CRC).
        var supplierName = $"Proveedor Reuse {Guid.NewGuid():N}"[..28];
        var supplierA = new SupplierPage(Page);
        await supplierA.NavigateToAddAsync(appId, itemA, BaseUrl);
        await supplierA.FillSupplierFormAsync(
            $"RQA-{appId}", supplierName, 900m, "2027-12-31", _quotationFile);
        await supplierA.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Reuse A's quotation on item B with B's own price (1500).
        var supplierB = new SupplierPage(Page);
        await supplierB.NavigateToAddAsync(appId, itemB, BaseUrl);
        await Expect(supplierB.ReuseCard).ToBeVisibleAsync();
        await supplierB.ReuseFirstAndFillAsync(1500m, "2027-12-31");
        await supplierB.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Both items now carry exactly one quotation; B shares A's supplier name.
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var rowA = Page.Locator("[data-testid=application-edit-item-row]", new() { HasTextString = "Línea A" });
        var rowB = Page.Locator("[data-testid=application-edit-item-row]", new() { HasTextString = "Línea B" });
        await Expect(rowA.Locator("td").Nth(2)).ToHaveTextAsync("1");
        await Expect(rowB.Locator("td").Nth(2)).ToHaveTextAsync("1");

        // Two independent quotation rows exist; the reused one shares A's supplier and
        // is downloadable (it shares A's uploaded document). Prices are asserted on the
        // decimal tail to stay agnostic to the es-CR thousands separator (a no-break
        // space) and to quotation-row ordering.
        var itemsCard = Page.Locator("[data-testid=application-edit-items-card]");
        await Expect(Page.Locator("[data-testid=application-edit-quotations-row]")).ToHaveCountAsync(2);
        await Expect(itemsCard).ToContainTextAsync(supplierName);
        await Expect(Page.Locator("[data-testid^=quotation-row-download-]").First).ToBeVisibleAsync();
        // A keeps its own 900,00; B carries its own 1 500,00 ("500,00" tail).
        await Expect(itemsCard).ToContainTextAsync("900,00");
        await Expect(itemsCard).ToContainTextAsync("500,00");
    }

    [Test]
    public async Task Reuse_OffersOnlySiblingQuotations_WithinApplication()
    {
        var appId = await CreateDraftAsync("reuse_scope");
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Línea A", 0, "Specs", BaseUrl);
        await itemPage.AddItemAsync(appId, "Línea B", 0, "Specs", BaseUrl);
        var ids = await GetItemIdsAsync(appId);
        var (itemA, itemB) = (ids[0], ids[1]);

        // Quote only item A.
        var supplierA = new SupplierPage(Page);
        await supplierA.NavigateToAddAsync(appId, itemA, BaseUrl);
        await supplierA.FillSupplierFormAsync(
            $"RQS-{appId}", $"Prov {appId}", 1000m, "2027-12-31", _quotationFile);
        await supplierA.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Item B sees exactly one reuse candidate (A's quotation).
        var supplierB = new SupplierPage(Page);
        await supplierB.NavigateToAddAsync(appId, itemB, BaseUrl);
        await Expect(supplierB.ReuseCard).ToBeVisibleAsync();
        Assert.That(await supplierB.ReuseOptionCountAsync(), Is.EqualTo(1));

        // Item A (the only quoted item) has no sibling quotations → no reuse card.
        var supplierAReuse = new SupplierPage(Page);
        await supplierAReuse.NavigateToAddAsync(appId, itemA, BaseUrl);
        await Expect(supplierAReuse.ReuseCard).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Reuse_SameSourceOntoTwoItems_YieldsTwoIndependentLineItems()
    {
        // SC-002 — a multi-product vendor quote is captured as one reused line item
        // per product; the model gives no way to lump products into one line.
        var appId = await CreateDraftAsync("reuse_nproducts");
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Producto 1", 0, "Specs", BaseUrl);
        await itemPage.AddItemAsync(appId, "Producto 2", 0, "Specs", BaseUrl);
        await itemPage.AddItemAsync(appId, "Producto 3", 0, "Specs", BaseUrl);
        var ids = await GetItemIdsAsync(appId);

        // One vendor quote uploaded on Producto 1.
        var supplier1 = new SupplierPage(Page);
        await supplier1.NavigateToAddAsync(appId, ids[0], BaseUrl);
        await supplier1.FillSupplierFormAsync(
            $"RQN-{appId}", $"Multi {appId}", 500m, "2027-12-31", _quotationFile);
        await supplier1.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Reuse the same source onto Producto 2 and Producto 3, each its own price.
        var supplier2 = new SupplierPage(Page);
        await supplier2.NavigateToAddAsync(appId, ids[1], BaseUrl);
        await supplier2.ReuseFirstAndFillAsync(600m, "2027-12-31");
        await supplier2.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var supplier3 = new SupplierPage(Page);
        await supplier3.NavigateToAddAsync(appId, ids[2], BaseUrl);
        await supplier3.ReuseFirstAndFillAsync(700m, "2027-12-31");
        await supplier3.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Three separate line items, each with its own single quotation/price.
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        await Expect(Page.Locator("[data-testid=application-edit-item-row]")).ToHaveCountAsync(3);
        await Expect(Page.Locator("[data-testid=application-edit-quotations-row]")).ToHaveCountAsync(3);
        var itemsCard = Page.Locator("[data-testid=application-edit-items-card]");
        await Expect(itemsCard).ToContainTextAsync("600,00");
        await Expect(itemsCard).ToContainTextAsync("700,00");
    }
}
