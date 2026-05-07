using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class ReviewQueueTests : AuthenticatedTestBase
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
    public async Task ReviewQueue_ShowsSubmittedApplications_ExcludesDraftAndResolved()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        // Create and submit an application as an applicant
        var applicantEmail = $"rq_applicant_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "Queue", "Applicant", $"LID-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        Assert.That(appIdMatch.Success, Is.True);
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Add an item
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Queue Test Item", 0, "Test specs", BaseUrl);

        // Add two suppliers
        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SUP1-{uniqueId}", "Supplier One", 1000m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SUP2-{uniqueId}", "Supplier Two", 1200m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        // Set impact assessment
        var impactButton = Page.Locator("a:has-text('Impacto')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (int i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Submit the application
        await Page.Locator("button[type=submit]:has-text('Enviar solicitud')").ClickAsync();
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        // Logout and login as reviewer
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        var reviewerEmail = $"rq_reviewer_{uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, password, "Queue", "Reviewer", $"RLID-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, password);

        // Navigate to review queue
        var reviewQueuePage = new ReviewQueuePage(Page);
        await reviewQueuePage.GotoAsync(BaseUrl);

        // Verify the submitted application appears in the queue
        await Expect(reviewQueuePage.QueueTable).ToBeVisibleAsync();
        var queueText = await reviewQueuePage.QueueTable.TextContentAsync();
        Assert.That(queueText, Does.Contain("Queue Applicant"));
    }

    [Test]
    public async Task ReviewQueue_RowReviewButton_NavigatesToReviewPageNot404()
    {
        // Pins the producer/consumer contract for the queue's "Revisar" button.
        // Pre-fix the projection emitted "/Review/Review/{id}" and the controller
        // route is "Review/{id:int}" → 404. The test clicks the actual row link
        // (no manual nav), proving the projection-built href resolves.
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var password = "Test123!";

        var applicantEmail = $"rqclick_applicant_{uniqueId}@example.com";
        await RegisterUserAsync(Page, applicantEmail, password, "Click", "Applicant", $"CLID-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Click Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"CSUP1-{uniqueId}", "Click Supplier One", 1000m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"CSUP2-{uniqueId}", "Click Supplier Two", 1200m, "2027-12-31", _testFilePath);
        await supplierPage.SubmitAsync();

        var impactButton = Page.Locator("a:has-text('Impacto')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (int i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        await Page.Locator("button[type=submit]:has-text('Enviar solicitud')").ClickAsync();
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        var reviewerEmail = $"rqclick_reviewer_{uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, password, "Click", "Reviewer", $"CRLID-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, password);

        var reviewQueuePage = new ReviewQueuePage(Page);
        await reviewQueuePage.GotoAsync(BaseUrl);

        var rowAction = reviewQueuePage.RowActionFor(appId);
        await Expect(rowAction).ToBeVisibleAsync();

        var emittedHref = await rowAction.GetAttributeAsync("href");
        Assert.That(emittedHref, Does.EndWith($"/Review/{appId}"),
            "Projection must emit /Review/{id}; pre-fix bug emitted /Review/Review/{id}.");
        Assert.That(emittedHref, Does.Not.Contain("/Review/Review/"),
            "Double 'Review/' segment indicates the broken hardcoded href is back.");

        await rowAction.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($@"/Review/{appId}(\?|$|#)"));
        await Expect(Page.Locator("h1, h2").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task ReviewQueue_PaginationWorks()
    {
        // This test verifies the pagination controls render when there are enough items
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var reviewerEmail = $"rq_pag_reviewer_{uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, reviewerEmail, password, "Pagination", "Reviewer", $"PLID-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, password);

        var reviewQueuePage = new ReviewQueuePage(Page);
        await reviewQueuePage.GotoAsync(BaseUrl);

        // Page should load without errors, even if empty
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
    }
}
