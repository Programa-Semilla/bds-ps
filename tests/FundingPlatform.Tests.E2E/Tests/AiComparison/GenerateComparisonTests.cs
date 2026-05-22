using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.AiComparison;

/// <summary>
/// Spec 020 / US1 — reviewer clicks "Generar comparación" on an item with
/// 2+ suppliers and sees the comparison region render with the stub-backed
/// canned artifact (table + es-CR narrative sections). Single-supplier items
/// show the explanatory tooltip instead of the button.
/// </summary>
public class GenerateComparisonTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test-quotation-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Test quotation document content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    [Test]
    public async Task ReviewerClicksGenerarComparacion_RendersComparisonTable()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var password = "Test123!";

        var applicantEmail = $"cmp_applicant_{uniqueId}@example.com";
        await RegisterUserAsync(Page, applicantEmail, password, "Cmp", "Applicant", $"LID-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Bomba centrífuga", 0, "1HP, acero", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SUP1-{uniqueId}", "Proveedor Económico", 120000m, "2027-12-31", _testFilePath,
            contactName: "Contacto 1", email: "p1@test.com");
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SUP2-{uniqueId}", "Proveedor Premium", 165000m, "2027-12-31", _testFilePath,
            contactName: "Contacto 2", email: "p2@test.com");
        await supplierPage.SubmitAsync();

        // Set impact assessment so the application can be submitted.
        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        var reviewerEmail = $"cmp_reviewer_{uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, password, "Cmp", "Reviewer", $"RLID-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, password);

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        // Confirm the Generar comparación button is rendered for the multi-supplier item.
        var generateBtn = Page.Locator("[data-testid='comparison-generate-btn']").First;
        await Expect(generateBtn).ToBeVisibleAsync();
        await Expect(generateBtn).ToHaveTextAsync(new Regex("Generar comparación"));

        await generateBtn.ClickAsync();

        // The JS handler reloads on success — the page should then carry the
        // comparison table with both suppliers as columns + narrative sections.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 60_000 });
        var table = Page.Locator("[data-testid='comparison-table']").First;
        await Expect(table).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(table).ToContainTextAsync("Proveedor Económico");
        await Expect(table).ToContainTextAsync("Proveedor Premium");

        // The es-CR narrative panel renders the cheapest/most-expensive call-out.
        await Expect(Page.Locator(".comparison-narratives")).ToContainTextAsync("Análisis de Costos");
    }
}
