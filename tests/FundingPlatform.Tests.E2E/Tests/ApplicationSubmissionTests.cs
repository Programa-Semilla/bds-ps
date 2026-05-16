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
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        Assert.That(appIdMatch.Success, Is.True, "Should be on application details page with ID");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Verify Draft status
        var draftBadge = Page.Locator("[data-testid=status-pill]:has-text('Borrador')");
        await Expect(draftBadge).ToBeVisibleAsync();

        // Add an item
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Submission Test Laptop", 0, "Intel i7, 16GB RAM", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

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
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

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
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Set impact assessment
        var impactButton = Page.Locator("a:has-text('Impacto')").First;
        await impactButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/\d+/Impact"));

        var templateSelector = Page.Locator("#templateSelector");
        await Expect(templateSelector).ToBeVisibleAsync();
        var options = await templateSelector.Locator("option").AllAsync();
        Assert.That(options.Count, Is.GreaterThan(1), "Should have at least one template option");
        var firstOptionValue = await options[1].GetAttributeAsync("value");
        await templateSelector.SelectOptionAsync(firstOptionValue!);

        var parameterFields = Page.Locator(".parameter-field");
        await Expect(parameterFields.First).ToBeVisibleAsync();

        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (int i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            switch (inputType)
            {
                case "number":
                    await input.FillAsync("100");
                    break;
                case "date":
                    await input.FillAsync("2026-12-31");
                    break;
                default:
                    await input.FillAsync("Test value");
                    break;
            }
        }

        var saveImpactButton = Page.Locator("button[type=submit]:has-text('Guardar impacto')");
        await saveImpactButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Verify item has complete impact
        var completeBadge = Page.Locator("table tbody tr:has-text('Submission Test Laptop') .status:has-text('Completo')");
        await Expect(completeBadge).ToBeVisibleAsync();

        // Submit the application
        var submitButton = Page.Locator("button[type=submit]:has-text('Enviar solicitud')");
        await Expect(submitButton).ToBeVisibleAsync();
        await submitButton.ClickAsync();

        // Verify redirect to details with success message
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        var successAlert = Page.Locator($".alert-success:has-text('{UiCopy.ApplicationSubmittedSuccess}')").First;
        await Expect(successAlert).ToBeVisibleAsync();

        // Verify state changed to Submitted
        var submittedBadge = Page.Locator("[data-testid=status-pill]:has-text('Enviada')");
        await Expect(submittedBadge).ToBeVisibleAsync();

        // Verify submit button is no longer visible
        var submitButtonAfter = Page.Locator("button[type=submit]:has-text('Enviar solicitud')");
        await Expect(submitButtonAfter).Not.ToBeVisibleAsync();
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
        var appIdMatch = Regex.Match(url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        // Add an item but no quotations
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Item Without Quotations", 0, "Some specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Try to submit
        var submitButton = Page.Locator("button[type=submit]:has-text('Enviar solicitud')");
        await Expect(submitButton).ToBeVisibleAsync();
        await submitButton.ClickAsync();

        // Verify error messages are shown
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        var errorAlert = Page.Locator(".alert-danger");
        await Expect(errorAlert).ToBeVisibleAsync();

        // Verify specific error about quotations
        var quotationError = Page.Locator(".alert-danger:has-text('quotation')");
        await Expect(quotationError).ToBeVisibleAsync();

        // Verify state is still Draft
        var draftBadge = Page.Locator("[data-testid=status-pill]:has-text('Borrador')");
        await Expect(draftBadge).ToBeVisibleAsync();
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

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        // Try to submit (button should be visible even with 0 items for error feedback)
        var submitButton = Page.Locator("button[type=submit]:has-text('Enviar solicitud')");
        await Expect(submitButton).ToBeVisibleAsync();
        await submitButton.ClickAsync();

        // Verify error messages are shown
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        var errorAlert = Page.Locator(".alert-danger");
        await Expect(errorAlert).ToBeVisibleAsync();

        // Verify specific error about items
        var itemError = Page.Locator(".alert-danger:has-text('at least one item')");
        await Expect(itemError).ToBeVisibleAsync();

        // Verify state is still Draft
        var draftBadge = Page.Locator("[data-testid=status-pill]:has-text('Borrador')");
        await Expect(draftBadge).ToBeVisibleAsync();
    }
}
