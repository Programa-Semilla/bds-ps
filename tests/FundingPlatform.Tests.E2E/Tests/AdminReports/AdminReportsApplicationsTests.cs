using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.Helpers;
using FundingPlatform.Tests.E2E.PageObjects.Admin.Reports;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.AdminReports;

[Category("AdminReports")]
public class AdminReportsApplicationsTests : AuthenticatedTestBase
{
    private const string AdminEmail = "admin@programa-semilla.test";
    private const string AdminPassword = "Sentinel123!";

    [Test]
    public async Task Applications_RendersFilterFormAndTable()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var page = new AdminReportsApplicationsPage(Page);
        await page.GoToAsync(BaseUrl);

        await Expect(page.FilterForm).ToBeVisibleAsync();
        await Expect(page.SearchInput).ToBeVisibleAsync();
        await Expect(page.ExportLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task Applications_DeepLink_RoundTripsFilterValues()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var page = new AdminReportsApplicationsPage(Page);
        await page.GoToWithQueryAsync(BaseUrl, "search=alpha");

        Assert.That(await page.SearchInput.InputValueAsync(), Is.EqualTo("alpha"),
            "Filter querystring must round-trip through the form on render.");
    }

    [Test]
    public async Task Applications_CsvExport_HasExpectedHeader()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var page = new AdminReportsApplicationsPage(Page);
        await page.GoToAsync(BaseUrl);

        var downloadTask = Page.WaitForDownloadAsync();
        await page.ExportLink.ClickAsync();
        var download = await downloadTask;

        var path = await download.PathAsync();
        Assert.That(path, Is.Not.Null.And.Not.Empty);
        var bytes = await File.ReadAllBytesAsync(path!);

        // Spec 015 / T414 — three back-compat columns appended at the end of every
        // admin-reports CSV so legacy consumers see the same prefix and new
        // consumers can read the original-currency / converted-CRC pair.
        CsvAssertions.AssertHeaderEquals(bytes,
            "App Id", "Applicant Name", "Applicant Legal Id", "State",
            "Created", "Submitted", "Resolved", "Item Count",
            "Approved Amount", "Currency", "Has Agreement", "Has Active Appeal",
            "OriginalCurrencyCode", "OriginalAmount", "ConvertedCrcAmount", "Fund");
    }
}
