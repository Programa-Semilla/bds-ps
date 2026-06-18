using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Application;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 038 (US3) — an Auditor flags a provider with a warning + note; reviewers
/// see it during application review (read-only); it never blocks the application.
/// </summary>
public class ProviderWarningTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFilePath = string.Empty;

    [SetUp]
    public void WriteQuotationFile()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"pw-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void DeleteQuotationFile()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
    }

    [Test]
    public async Task AuditorWarning_VisibleToReviewer_NonBlocking()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"pw_applicant_{uid}@example.com";
        var reviewerEmail = $"pw_reviewer_{uid}@example.com";
        var supplierName = $"Proveedor Warn {uid}";
        var note = $"Revisar contrato {uid}";

        // ----- Applicant creates an application with a supplier, submits for review. -----
        await RegisterUserAsync(Page, applicantEmail, Password, "Warn", "Applicant", $"PWA-{uid}");
        await LoginAsync(Page, applicantEmail, Password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Warn Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"PW1-{uid}", supplierName, 900m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        // Second supplier so the item meets the minimum-quotations rule and can submit.
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"PW2-{uid}", $"Proveedor Alt {uid}", 1100m, "2027-12-31", _quotationFilePath);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // ----- Auditor flags the provider with a warning + note. -----
        var supplierId = await SupplierSeed.GetSupplierIdByNameAsync(ConnectionString, supplierName);
        Assert.That(supplierId, Is.GreaterThan(0), "seeded supplier should be resolvable by name");

        await LoginAsync(Page, "auditor@programa-semilla.test", "Demo123!");
        await Page.GotoAsync($"{BaseUrl}/Admin/Suppliers/{supplierId}");
        await Page.Locator("[data-testid=\"admin-supplier-warning-toggle\"]").CheckAsync();
        await Page.Locator("[data-testid=\"admin-supplier-warning-note\"]").FillAsync(note);
        await Page.Locator("[data-testid=\"admin-supplier-edit-submit\"]").ClickAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // ----- Reviewer sees the warning during review; the application still advances. -----
        await RegisterUserAsync(Page, reviewerEmail, Password, "Warn", "Reviewer", $"PWR-{uid}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        await Expect(Page.Locator("[data-testid=\"supplier-warning-banner\"]").First).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"supplier-warning-note\"]").First).ToContainTextAsync(note);

        // Non-blocking: the review decision controls are present (the warning did not
        // gate the review surface). Reviewers cannot edit the warning here.
        await Expect(Page.Locator("[data-testid=\"review-quotation-row\"]").First).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid=\"admin-supplier-warning-toggle\"]")).ToHaveCountAsync(0);
    }
}
