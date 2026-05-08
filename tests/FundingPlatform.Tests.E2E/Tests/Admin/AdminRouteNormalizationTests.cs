using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / US5 / FR-018..FR-020 — old `Admin/Admin*` paths return 404 with
/// no redirect shim; the normalized `/Admin/{Name}` paths return 200 for an
/// authenticated Admin.
/// </summary>
public class AdminRouteNormalizationTests : AuthenticatedTestBase
{
    private async Task RegisterAndLoginAsAdminAsync(IPage page, string email, string password)
    {
        await RegisterUserAsync(page, email, password, "Admin", "Tester", $"LID-{Guid.NewGuid():N}"[..16]);
        await page.GotoAsync($"{BaseUrl}/Account/Login");
        var token = await page.Locator("input[name='__RequestVerificationToken']").GetAttributeAsync("value");
        var formData = page.APIRequest.CreateFormData();
        formData.Set("email", email);
        formData.Set("__RequestVerificationToken", token ?? "");
        var response = await page.APIRequest.PostAsync($"{BaseUrl}/Account/PromoteToAdmin", new()
        {
            Form = formData
        });
        Assert.That(response.Ok, Is.True, "Failed to promote user to admin");
        await LoginAsync(page, email, password);
    }

    [TestCase("/Admin/AdminCurrencies")]
    [TestCase("/Admin/AdminExchangeRates")]
    [TestCase("/Admin/AdminLegacyQuotations")]
    public async Task LegacyAdminPrefixedRoutes_Return404(string oldPath)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_route_{uniqueId}@example.com", "Test123!");

        var resp = await Page.APIRequest.GetAsync($"{BaseUrl}{oldPath}");
        Assert.That(resp.Status, Is.EqualTo(404),
            $"FR-020: old path {oldPath} must 404 with no redirect shim.");
    }

    [TestCase("/Admin/Currencies")]
    [TestCase("/Admin/ExchangeRates")]
    [TestCase("/Admin/LegacyQuotations")]
    public async Task NormalizedAdminRoutes_Return200(string newPath)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_norm_{uniqueId}@example.com", "Test123!");

        var resp = await Page.GotoAsync($"{BaseUrl}{newPath}");
        Assert.That(resp?.Status, Is.EqualTo(200),
            $"FR-018: normalized route {newPath} must serve a 200.");
    }

    [Test]
    public async Task SidebarAdminEntries_LinkToNormalizedRoutes()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        await RegisterAndLoginAsAdminAsync(Page, $"admin_sidebar_{uniqueId}@example.com", "Test123!");
        await Page.GotoAsync($"{BaseUrl}/Admin");

        var currencies = await Page.Locator("[data-testid=sidebar-entry-currencies]").GetAttributeAsync("href");
        var rates = await Page.Locator("[data-testid=sidebar-entry-exchange-rates]").GetAttributeAsync("href");
        var legacy = await Page.Locator("[data-testid=sidebar-entry-legacy-quotations]").GetAttributeAsync("href");

        Assert.Multiple(() =>
        {
            Assert.That(currencies, Is.EqualTo("/Admin/Currencies"));
            Assert.That(rates, Is.EqualTo("/Admin/ExchangeRates"));
            Assert.That(legacy, Is.EqualTo("/Admin/LegacyQuotations"));
        });
    }
}
