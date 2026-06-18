using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 039 / US5 / FR-025 / SC-006 — the add-item form presents the product name
/// before the category selector, then the dynamic category-fields container.
/// </summary>
public class ItemFieldOrderTests : AuthenticatedTestBase
{
    [Test]
    public async Task AddItemForm_ProductNamePrecedesCategory_ThenDynamicFields()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"order_app_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Order", "Applicant", $"ORD-{uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        await Page.GotoAsync($"{BaseUrl}/Application/{appId}/Item/Add");

        // SC-006 — product name renders before the category selector.
        var productBeforeCategory = await Page.EvaluateAsync<bool>(@"() => {
            const pn = document.querySelector('[data-field=ProductName]');
            const cat = document.querySelector('[data-field=CategoryId]');
            if (!pn || !cat) return false;
            return (pn.compareDocumentPosition(cat) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
        }");
        Assert.That(productBeforeCategory, Is.True, "Product name must precede the category selector.");

        // The dynamic category-fields container renders after the category selector.
        var categoryBeforeDynamic = await Page.EvaluateAsync<bool>(@"() => {
            const cat = document.querySelector('[data-field=CategoryId]');
            const dyn = document.getElementById('category-fields');
            if (!cat || !dyn) return false;
            return (cat.compareDocumentPosition(dyn) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
        }");
        Assert.That(categoryBeforeDynamic, Is.True, "Category selector must precede the dynamic fields container.");
    }
}
