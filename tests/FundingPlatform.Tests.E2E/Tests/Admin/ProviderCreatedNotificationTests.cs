using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Application;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 038 (US4) — creating a provider emails every Auditor (allowlist-honored,
/// best-effort). The seeded demo auditor (<c>auditor@programa-semilla.test</c>,
/// in the non-prod allowlist) is the captured recipient.
/// </summary>
public class ProviderCreatedNotificationTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void WriteQuotationFile()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"pcn-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void DeleteQuotationFile()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    [Test]
    public async Task ApplicantCreatesProvider_AuditorReceivesEmailWithLink()
    {
        if (MailCapture is null)
            Assert.Ignore("smtp4dev sidecar unavailable; mail capture not possible.");

        await MailCapture.DrainAsync();

        var uid = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"pcn_applicant_{uid}@example.com";
        var supplierName = $"Proveedor Notif {uid}";

        await RegisterUserAsync(Page, applicantEmail, Password, "Prov", "Applicant", $"PCN-{uid}");
        await LoginAsync(Page, applicantEmail, Password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "PCN Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"PCN1-{uid}", supplierName, 900m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        var captured = await MailCapture.WaitForAsync(
            minCount: 1,
            timeout: TimeSpan.FromSeconds(30),
            filter: m => m.ToAddresses.Any(a => a.Contains("auditor@programa-semilla.test", StringComparison.OrdinalIgnoreCase))
                         && m.Subject.Contains("Nuevo proveedor para revisar", StringComparison.Ordinal));

        var message = captured[0];
        Assert.That(message.Subject, Does.Contain(supplierName));
        Assert.That(message.HtmlBody + message.TextBody, Does.Contain("/Admin/Suppliers/"));
    }
}
