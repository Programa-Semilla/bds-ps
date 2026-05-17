using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

public class ApplicationSubmissionTests : AuthenticatedTestBase
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
        {
            File.Delete(_testFilePath);
        }
    }

    [Test]
    public async Task SubmitApplication_Successfully()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"submit_ok_{uniqueId}@example.com";
        var password = "Test123!";

        // Register and login
        await RegisterUserAsync(Page, email, password, "Submit", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        // Create application
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        Assert.That(appIdMatch.Success, Is.True, "Should be on draft editor page with ID");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Add an item
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Submission Test Laptop", 0, "Intel i7, 16GB RAM", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Add first supplier with quotation
        var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").First;
        await Expect(addSupplierLink).ToBeVisibleAsync();
        await addSupplierLink.ClickAsync();

        var supplierPage = new SupplierPage(Page);
        await supplierPage.FillSupplierFormAsync(
            legalId: $"SUP1-{uniqueId}",
            name: "Supplier One",
            price: 1000.00m,
            validUntil: "2027-12-31",
            filePath: _testFilePath,
            contactName: "Contact One",
            email: "sup1@test.com",
            phone: "555-0001",
            location: "Location One");
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Add second supplier with quotation (MinQuotationsPerItem = 2)
        var addSupplierLink2 = Page.Locator("a:has-text('Agregar proveedor')").First;
        await Expect(addSupplierLink2).ToBeVisibleAsync();
        await addSupplierLink2.ClickAsync();

        await supplierPage.FillSupplierFormAsync(
            legalId: $"SUP2-{uniqueId}",
            name: "Supplier Two",
            price: 1200.00m,
            validUntil: "2027-12-31",
            filePath: _testFilePath,
            contactName: "Contact Two",
            email: "sup2@test.com",
            phone: "555-0002",
            location: "Location Two");
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Set impact assessment via the draft editor's Impact card.
        await SetImpactFromEditAsync(appId);

        // The editor's Impact card now reads "Definido".
        await Expect(Page.Locator("[data-testid=application-edit-impact-status]")).ToContainTextAsync("Definido");

        // Submit the application through the gated editor button → /review.
        await SubmitDraftViaReviewAsync(appId);

        // Lands on the read-only Details summary with success message.
        var successAlert = Page.Locator($".alert-success:has-text('{UiCopy.ApplicationSubmittedSuccess}')").First;
        await Expect(successAlert).ToBeVisibleAsync();

        // Verify state changed to Submitted
        var submittedBadge = Page.Locator("[data-testid=status-pill]:has-text('Enviada')");
        await Expect(submittedBadge).ToBeVisibleAsync();
    }

    [Test]
    public async Task SubmitApplication_WithMissingQuotations_ShowsErrors()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"submit_noq_{uniqueId}@example.com";
        var password = "Test123!";

        // Register and login
        await RegisterUserAsync(Page, email, password, "NoQuot", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        // Create application
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var url = Page.Url;
        var appIdMatch = Regex.Match(url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Add an item but no quotations
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Item Without Quotations", 0, "Some specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Spec 021 / US2 / FR-017 — an incomplete draft (no quotations) cannot be
        // submitted: the gated editor submit button stays disabled.
        var submitButton = Page.Locator("[data-testid=application-edit-submit]");
        await Expect(submitButton).ToBeVisibleAsync();
        await Expect(submitButton).ToBeDisabledAsync();
    }

    [Test]
    public async Task SubmitApplication_WithNoItems_ShowsErrors()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"submit_noi_{uniqueId}@example.com";
        var password = "Test123!";

        // Register and login
        await RegisterUserAsync(Page, email, password, "NoItems", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        // Create application (empty, no items)
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Spec 021 / US2 / FR-017 — an empty draft (no items) cannot be submitted:
        // the gated editor submit button stays disabled.
        var submitButton = Page.Locator("[data-testid=application-edit-submit]");
        await Expect(submitButton).ToBeVisibleAsync();
        await Expect(submitButton).ToBeDisabledAsync();
    }
}
