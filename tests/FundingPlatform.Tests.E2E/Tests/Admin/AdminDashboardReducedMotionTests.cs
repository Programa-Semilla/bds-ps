using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 017 / FR-002 + research §9 — when reduced-motion is enabled the KPI
/// tickers MUST render their final values immediately. The motion.js
/// implementation guarantees this; this test pins the behavior at the user
/// surface so a regression in motion handling is caught.
/// </summary>
public class AdminDashboardReducedMotionTests : AuthenticatedTestBase
{
    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ReducedMotion = ReducedMotion.Reduce,
        };
    }

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

    [Test]
    public async Task ReducedMotion_KpiTickers_RenderFinalValueImmediately()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin_rm_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterAndLoginAsAdminAsync(Page, email, password);

        var dashboard = new AdminDashboardPage(Page);
        await dashboard.GotoAsync(BaseUrl);

        // Inspect the underlying ticker target attribute and the rendered text;
        // under reduced-motion they must match (no in-flight animation tween).
        var node = dashboard.KpiNumeric("active-users");
        await Expect(node).ToBeVisibleAsync();
        var target = await node.GetAttributeAsync("data-ticker-target");
        var text = (await node.InnerTextAsync()).Trim().Replace(",", string.Empty).Replace(".", string.Empty);
        Assert.That(text, Is.EqualTo(target?.Replace(",", string.Empty).Replace(".", string.Empty)),
            "Reduced-motion → ticker target equals rendered text on first paint.");
    }
}
