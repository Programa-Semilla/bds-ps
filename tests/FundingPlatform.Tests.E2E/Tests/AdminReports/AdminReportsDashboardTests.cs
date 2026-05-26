using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin.Reports;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.AdminReports;

[Category("AdminReports")]
public class AdminReportsDashboardTests : AuthenticatedTestBase
{
    private const string AdminEmail = "admin@programa-semilla.test";
    private const string AdminPassword = "Sentinel123!";

    [Test]
    public async Task Dashboard_RendersThreeKpiRowsAndSubTabStrip()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var dashboard = new AdminReportsDashboardPage(Page);
        await dashboard.GoToAsync(BaseUrl);

        await Expect(dashboard.PipelineRow).ToBeVisibleAsync();
        await Expect(dashboard.FinancialRow).ToBeVisibleAsync();
        await Expect(dashboard.ApplicantsRow).ToBeVisibleAsync();
        await Expect(dashboard.SubTabs).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_DateRangePicker_RoundTripsValues()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var dashboard = new AdminReportsDashboardPage(Page);
        await dashboard.GoToWithRangeAsync(BaseUrl, "2025-01-01", "2025-01-31");

        Assert.That(await dashboard.FromInput.InputValueAsync(), Is.EqualTo("2025-01-01"));
        Assert.That(await dashboard.ToInput.InputValueAsync(), Is.EqualTo("2025-01-31"));
    }

    [Test]
    public async Task Dashboard_NonAdmin_RedirectsToAccessDenied()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"applicant_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Plain", "Applicant", $"PA-{uniqueId}");
        await LoginAsync(Page, email, password);

        await Page.GotoAsync($"{BaseUrl}/Admin/Reports");

        Assert.That(Page.Url, Does.Contain("AccessDenied").Or.Contain("Login"),
            "Non-admin must not see /Admin/Reports.");
    }

    [Test]
    public async Task Dashboard_PipelineTile_RendersAsNumericValue()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var dashboard = new AdminReportsDashboardPage(Page);
        await dashboard.GoToAsync(BaseUrl);

        // "Enviada" (es-CR for Submitted) is one of the always-rendered pipeline
        // states. Even on an empty seed it should render a numeric "0" in the tile
        // body. The pipeline tile label is now localized (StatusVisualMap), so the
        // tile is keyed by its Spanish label.
        var value = await dashboard.ReadKpiNumericAsync("Enviada");
        Assert.That(value, Is.Not.Null, "Enviada pipeline tile must render a numeric value.");
    }
}
