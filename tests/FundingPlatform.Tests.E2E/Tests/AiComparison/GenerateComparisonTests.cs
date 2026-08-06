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
        // Must be a real PDF, not text in a .pdf-named file. ComparisonOrchestrator
        // classifies attachments by magic bytes and refuses anything that is not a
        // PDF or a supported image, because sending non-PDF bytes as
        // application/pdf fails the whole extract call at the provider. A text
        // placeholder passed here only because the Stub provider ignores content —
        // the same upload would have 400'd against the live API.
        File.WriteAllBytes(_testFilePath, MinimalPdfBytes());
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    /// <summary>
    /// A single-page PDF: correct <c>%PDF-</c> header, object graph and xref, so it
    /// is a genuine PDF rather than a file that merely starts with the signature.
    /// </summary>
    private static byte[] MinimalPdfBytes()
    {
        var objects = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
        };

        var body = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>(objects.Length);
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(body.Length);
            body.Append(i + 1).Append(" 0 obj").Append(objects[i]).Append("endobj\n");
        }

        var xrefOffset = body.Length;
        body.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        body.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            body.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        body.Append("trailer<</Size ").Append(objects.Length + 1).Append("/Root 1 0 R>>\n")
            .Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");

        return System.Text.Encoding.ASCII.GetBytes(body.ToString());
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

        // The comparison header must be READABLE: text color and background must contrast
        // (regression — the header previously rendered white text on a light surface because
        // comparison.css overrode the brand dark-teal header background without a text color).
        var lumDelta = await Page.Locator("[data-testid='comparison-table'] thead th").First.EvaluateAsync<double>(@"
            th => {
                const cs = getComputedStyle(th);
                const lum = (rgb) => {
                    const m = rgb.match(/\d+(\.\d+)?/g).map(Number);
                    const f = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
                    return 0.2126 * f(m[0]) + 0.7152 * f(m[1]) + 0.0722 * f(m[2]);
                };
                return Math.abs(lum(cs.color) - lum(cs.backgroundColor));
            }");
        Assert.That(lumDelta, Is.GreaterThan(0.4),
            "Comparison header text/background must contrast (luminance delta > 0.4).");
    }
}
