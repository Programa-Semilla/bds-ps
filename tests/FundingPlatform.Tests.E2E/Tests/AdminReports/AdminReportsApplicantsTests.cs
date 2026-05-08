using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.Helpers;
using FundingPlatform.Tests.E2E.PageObjects.Admin.Reports;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.AdminReports;

[Category("AdminReports")]
public class AdminReportsApplicantsTests : AuthenticatedTestBase
{
    private const string AdminEmail = "admin@FundingPlatform.com";
    private const string AdminPassword = "Sentinel123!";

    [Test]
    public async Task Applicants_RendersFilterAndExport()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var page = new AdminReportsApplicantsPage(Page);
        await page.GoToAsync(BaseUrl);

        await Expect(page.FilterForm).ToBeVisibleAsync();
        await Expect(page.ExportLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task Applicants_CsvExport_HasExpectedHeader()
    {
        await LoginAsync(Page, AdminEmail, AdminPassword);

        var page = new AdminReportsApplicantsPage(Page);
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
            "Full Name", "Legal Id", "Email",
            "Total Apps", "Resolved Count", "Response Finalized Count", "Agreement Executed Count",
            "Approval Rate", "Approved Amount", "Executed Amount", "Currency", "Last Activity",
            "OriginalCurrencyCode", "OriginalAmount", "ConvertedCrcAmount");
    }
}
