using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 / US4 (SC-003) — the shared per-line decision summary presents the
/// identical field set (incl. technical specifications) for an approved line and
/// a rejected line on every interaction surface: the reviewer review screen, the
/// applicant accept/reject screen, and the funding-agreement Details page across
/// its generate / signing / signed-review states.
///
/// Quotes are CRC here for journey reliability; the non-CRC conversion-note
/// formatting is covered by <c>DecisionSummaryProjectionTests</c>.
/// </summary>
[Category("ReviewFundingUx")]
public class DecisionSummaryParityTests : AuthenticatedTestBase
{
    private string _quotationFilePath = string.Empty;
    private string _uniqueId = string.Empty;
    private string _applicantEmail = string.Empty;
    private string _reviewerEmail = string.Empty;
    private string _adminEmail = string.Empty;
    private readonly List<string> _seededFiles = [];
    private const string DefaultPassword = "Test123!";
    private const string RejectReason = "Fuera de presupuesto.";

    private SupplierPage _supplierPage = null!;

    [SetUp]
    public void SetUp()
    {
        _quotationFilePath = Path.Combine(Path.GetTempPath(), $"ds-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFilePath, "Quotation placeholder content");
        _supplierPage = new SupplierPage(Page);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_quotationFilePath)) File.Delete(_quotationFilePath);
        foreach (var path in _seededFiles)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        _seededFiles.Clear();
    }

    [Test]
    public async Task DecisionSummary_IsIdentical_AcrossAllFiveScreens()
    {
        var (appId, itemAId, itemBId) = await SeedTwoItemReviewedApplicationAsync();

        // Screen 1 — reviewer review screen (decisions captured, before finalize).
        await LoginAsync(Page, _reviewerEmail, DefaultPassword);
        await Page.GotoAsync($"{BaseUrl}/Review/{appId}");
        await AssertSummaryParityAsync("reviewer-review");

        await FinalizeAsReviewerAsync(appId);
        await Logout();

        // Screen 2 — applicant accept/reject screen.
        await LoginAsync(Page, _applicantEmail, DefaultPassword);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await AssertSummaryParityAsync("applicant-response");

        // The response form lists every item; answer each row so submit enables.
        var rows = await Page.Locator("tr.response-item").AllAsync();
        foreach (var row in rows)
        {
            await row.Locator("input.decision-accept").CheckAsync();
        }
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();
        await Logout();

        // Screens 3 & 4 — funding-agreement Details (generate + applicant signing
        // states are the same surface). Seed the generated agreement to skip the
        // Syncfusion PDF dependency.
        _seededFiles.Add(await FundingAgreementSeeder.SeedGeneratedAgreementAsync(
            ConnectionString, appId, _adminEmail, CreateBlobServiceClient()));

        await LoginAsync(Page, _adminEmail, DefaultPassword);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        await AssertSummaryParityAsync("fa-details-generate");
        await Logout();

        await LoginAsync(Page, _applicantEmail, DefaultPassword);
        await responsePage.GotoAsync(BaseUrl, appId); // applicant signing surface embeds the same panel
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        await AssertSummaryParityAsync("fa-details-signing");
        await Logout();

        // Screen 5 — reviewer signed-review state (post-execution).
        _seededFiles.Add(await FundingAgreementSeeder.SeedExecutedAgreementAsync(
            ConnectionString, appId, _adminEmail, _applicantEmail, _reviewerEmail, CreateBlobServiceClient()));

        await LoginAsync(Page, _reviewerEmail, DefaultPassword);
        await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
        await AssertSummaryParityAsync("fa-details-signed-review");
    }

    /// <summary>
    /// Asserts the identical field set on whichever surface is currently loaded:
    /// the approved line (Equipo A) shows code/product/category/status +
    /// supplier+amount; the rejected line (Equipo B) shows the same identity
    /// fields + reason + every quoted supplier. Spec 035 removed the free-text
    /// specs line from the lean decision summary (TechnicalSpecifications gone),
    /// so parity is asserted on the category instead.
    /// </summary>
    private async Task AssertSummaryParityAsync(string surface)
    {
        var summary = Page.Locator("[data-testid=decision-summary]").First;
        await Expect(summary).ToBeVisibleAsync();

        var lineA = summary.Locator("[data-testid=decision-summary-line]")
            .Filter(new LocatorFilterOptions { HasText = "Equipo A" });
        await Expect(lineA.Locator("[data-testid=decision-line-status]"))
            .ToHaveTextAsync("Aprobado");
        await Expect(lineA.Locator("[data-testid=decision-line-supplier]")).ToBeVisibleAsync();
        await Expect(lineA.Locator("[data-testid=decision-line-category]")).ToContainTextAsync("Computing Equipment");

        var lineB = summary.Locator("[data-testid=decision-summary-line]")
            .Filter(new LocatorFilterOptions { HasText = "Equipo B" });
        await Expect(lineB.Locator("[data-testid=decision-line-status]")).ToHaveTextAsync("Rechazado");
        await Expect(lineB.Locator("[data-testid=decision-line-reason]")).ToContainTextAsync(RejectReason);
        await Expect(lineB.Locator("[data-testid=decision-line-quotes]")).ToBeVisibleAsync();
        await Expect(lineB.Locator("[data-testid=decision-line-category]")).ToContainTextAsync("Computing Equipment");
        // Rejected line lists every quoted supplier (≥2 rows).
        Assert.That(await lineB.Locator("[data-testid=decision-line-quotes] tbody tr").CountAsync(),
            Is.GreaterThanOrEqualTo(2), $"[{surface}] rejected line must list all quoted suppliers");
    }

    private async Task FinalizeAsReviewerAsync(int appId)
    {
        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.FinalizeButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
    }

    private async Task Logout() =>
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

    private async Task<(int appId, int itemAId, int itemBId)> SeedTwoItemReviewedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        _applicantEmail = $"ds_applicant_{_uniqueId}@example.com";
        _reviewerEmail = $"ds_reviewer_{_uniqueId}@example.com";
        _adminEmail = $"ds_admin_{_uniqueId}@example.com";

        await RegisterUserAsync(Page, _adminEmail, DefaultPassword, "DS", "Admin", $"DSA-{_uniqueId}");
        await AssignRoleAsync(_adminEmail, "Admin");

        await RegisterUserAsync(Page, _applicantEmail, DefaultPassword, "DS", "Applicant", $"DSP-{_uniqueId}");
        await LoginAsync(Page, _applicantEmail, DefaultPassword);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Equipo A", 0, "Specs A", BaseUrl);
        await itemPage.AddItemAsync(appId, "Equipo B", 0, "Specs B", BaseUrl);

        // Two CRC suppliers per item (rows are ordered A, B by insertion).
        await AddSupplierToRowAsync(appId, rowIndex: 0, $"DA1-{_uniqueId}", "Proveedor A1", 900m);
        await AddSupplierToRowAsync(appId, rowIndex: 0, $"DA2-{_uniqueId}", "Proveedor A2", 1100m);
        await AddSupplierToRowAsync(appId, rowIndex: 1, $"DB1-{_uniqueId}", "Proveedor B1", 800m);
        await AddSupplierToRowAsync(appId, rowIndex: 1, $"DB2-{_uniqueId}", "Proveedor B2", 1200m);

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Logout();

        await RegisterUserAsync(Page, _reviewerEmail, DefaultPassword, "DS", "Reviewer", $"DSR-{_uniqueId}");
        await AssignRoleAsync(_reviewerEmail, "Reviewer");
        await LoginAsync(Page, _reviewerEmail, DefaultPassword);

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var itemAId = await ItemIdByProductAsync("Equipo A");
        var itemBId = await ItemIdByProductAsync("Equipo B");

        // Approve A (pick a supplier), reject B (with reason).
        await reviewPage.ItemDecisionRadio(itemAId, "Approve").CheckAsync();
        var dropdown = reviewPage.ItemSupplierDropdown(itemAId);
        var options = await dropdown.Locator("option").AllAsync();
        await dropdown.SelectOptionAsync(await options[1].GetAttributeAsync("value") ?? "");
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemAId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        await reviewPage.ItemDecisionRadio(itemBId, "Reject").CheckAsync();
        await reviewPage.ItemCommentField(itemBId).FillAsync(RejectReason);
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemBId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        await Logout();
        return (appId, itemAId, itemBId);
    }

    private async Task AddSupplierToRowAsync(int appId, int rowIndex, string legalSeed, string name, decimal price)
    {
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var row = Page.Locator("tr[data-testid=application-edit-item-row]").Nth(rowIndex);
        await row.Locator("a:has-text('Agregar proveedor')").ClickAsync();
        await _supplierPage.FillSupplierFormAsync(legalSeed, name, price, "2027-12-31", _quotationFilePath);
        await _supplierPage.SubmitAsync();
    }

    private async Task<int> ItemIdByProductAsync(string product)
    {
        var card = Page.Locator(".review-item").Filter(new LocatorFilterOptions { HasText = product }).First;
        var id = await card.GetAttributeAsync("data-item-id");
        return int.Parse(id!);
    }
}
