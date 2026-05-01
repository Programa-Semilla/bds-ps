using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace FundingPlatform.Tests.E2E.Fixtures;

public class AuthenticatedTestBase : PageTest
{
    private static readonly AspireFixture _fixture = new();
    private static bool _initialized;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    protected string BaseUrl => _fixture.BaseUrl;
    protected string ConnectionString => _fixture.ConnectionString;

    /// <summary>
    /// Spec 014 — exposes a configured <see cref="BlobServiceClient"/> so tests/seeders
    /// can write placeholder blobs to Azurite at the same canonical keys recorded on
    /// the seeded SQL rows. Returns null when the fixture fell back to filesystem mode.
    /// </summary>
    protected BlobServiceClient? CreateBlobServiceClient() => _fixture.CreateBlobServiceClient();

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        };
    }

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        // Playwright's default Expect timeout is 5s. The applicant impact-picker JS
        // (Views/Item/Impact.cshtml) fetches /Item/TemplateParameters/{id} on dropdown
        // change and renders .parameter-field after the response. Under shared-fixture
        // load (one Aspire container, ~20 test classes back-to-back), that fetch can
        // tip past 5s and time the assertion out before .parameter-field is rendered.
        // 15s gives enough headroom for transient slowness without masking real bugs.
        Assertions.SetDefaultExpectTimeout(15_000);

        await _initLock.WaitAsync();
        try
        {
            if (!_initialized)
            {
                await _fixture.StartAsync();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Pick the first real impact template and wait until the JS-rendered
    /// <c>.parameter-field</c> elements are in the DOM. Encapsulates two race
    /// hazards observed under shared-fixture load:
    ///   1) <see cref="ILocator.ClickAsync"/> on the Impact link does not block
    ///      until <c>DOMContentLoaded</c>, so the DCL handler in
    ///      <c>Views/Item/Impact.cshtml</c> that binds the dropdown's
    ///      <c>change</c> listener may not yet have run when we call
    ///      <see cref="ILocator.SelectOptionAsync"/>. Without an explicit DCL
    ///      wait, the change event is fired with no listener and no fetch
    ///      happens. Waiting for <see cref="LoadState.DOMContentLoaded"/>
    ///      guarantees the handler is bound before we interact.
    ///   2) The change handler issues an async fetch; pinning the action to
    ///      <see cref="IPage.RunAndWaitForResponseAsync"/> so the test only
    ///      proceeds once the response arrives, instead of relying on the
    ///      <c>Expect(.parameter-field).ToBeVisibleAsync()</c> 5s/15s timeout.
    /// </summary>
    protected async Task PickFirstImpactTemplateAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var templateSelector = Page.Locator("#templateSelector");
        await Expect(templateSelector).ToBeVisibleAsync();
        var options = await templateSelector.Locator("option").AllAsync();
        var value = await options[1].GetAttributeAsync("value");
        Assert.That(value, Is.Not.Null.And.Not.Empty,
            "Impact template options[1] must have a non-empty value (seed templates expected).");

        await Page.RunAndWaitForResponseAsync(
            async () => await templateSelector.SelectOptionAsync(value!),
            r => r.Url.Contains("/Item/TemplateParameters/"));

        await Expect(Page.Locator(".parameter-field").First).ToBeVisibleAsync();
    }

    protected async Task RegisterUserAsync(IPage page, string email, string password, string firstName, string lastName, string legalId)
    {
        await page.GotoAsync($"{BaseUrl}/Account/Register");
        await page.FillAsync("[name=Email]", email);
        await page.FillAsync("[name=Password]", password);
        await page.FillAsync("[name=ConfirmPassword]", password);
        await page.FillAsync("[name=FirstName]", firstName);
        await page.FillAsync("[name=LastName]", lastName);
        await page.FillAsync("[name=LegalId]", legalId);
        await page.Locator("form[action*='Account/Register'] button[type=submit]").ClickAsync();
    }

    protected async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync($"{BaseUrl}/Account/Login");
        await page.FillAsync("[name=Email]", email);
        await page.FillAsync("[name=Password]", password);
        await page.Locator("form[action*='Account/Login'] button[type=submit]").ClickAsync();
    }

    protected async Task AssignRoleAsync(string email, string role)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var response = await client.GetAsync($"/Account/AssignRole?email={Uri.EscapeDataString(email)}&role={Uri.EscapeDataString(role)}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Drives the UI through the full happy-path from application creation to
    /// <c>ResponseFinalized</c> state (item approved, review finalized, applicant accepted).
    /// No Funding Agreement is generated here.
    /// </summary>
    /// <param name="uniqueId">A short unique suffix (e.g. 8-hex chars) used to namespace
    /// all seeded users and legal IDs so parallel tests don't collide.</param>
    /// <param name="quotationFilePath">Path to a placeholder PDF file to attach as supplier quotations.</param>
    /// <returns>A tuple of (ApplicationId, ApplicantEmail, ApplicantPassword).</returns>
    protected async Task<(int ApplicationId, string ApplicantEmail, string ApplicantPassword)>
        CreateApplicationAndSubmitResponseAsync(string uniqueId, string quotationFilePath)
    {
        const string password = "Test123!";
        var applicantEmail = $"seed_applicant_{uniqueId}@example.com";
        var reviewerEmail = $"seed_reviewer_{uniqueId}@example.com";
        var adminEmail = $"seed_admin_{uniqueId}@example.com";

        await RegisterUserAsync(Page, adminEmail, password, "Seed", "Admin", $"SADM-{uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");

        await RegisterUserAsync(Page, applicantEmail, password, "Seed", "Applicant", $"SAPP-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Seed Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SQ1-{uniqueId}", "Supplier Alpha", 900m, "2027-12-31", quotationFilePath);
        await supplierPage.SubmitAsync();

        addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await supplierPage.FillSupplierFormAsync($"SQ2-{uniqueId}", "Supplier Beta", 1100m, "2027-12-31", quotationFilePath);
        await supplierPage.SubmitAsync();

        var impactButton = Page.Locator($"a:has-text('{UiCopy.Impact}')").First;
        await impactButton.ClickAsync();
        await PickFirstImpactTemplateAsync();
        var paramInputs = Page.Locator(".parameter-field input.form-control");
        var inputCount = await paramInputs.CountAsync();
        for (var i = 0; i < inputCount; i++)
        {
            var input = paramInputs.Nth(i);
            var inputType = await input.GetAttributeAsync("type");
            await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "Test value");
        }
        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SaveImpact}')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        await Page.Locator($"button[type=submit]:has-text('{UiCopy.SubmitApplication}')").ClickAsync();
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await RegisterUserAsync(Page, reviewerEmail, password, "Seed", "Reviewer", $"SREV-{uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, password);

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var suppOptions = await supplierDropdown.Locator("option").AllAsync();
        await supplierDropdown.SelectOptionAsync(await suppOptions[1].GetAttributeAsync("value") ?? "");
        await reviewPage.ItemSubmitButton(itemId).ClickAsync();
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        await reviewPage.FinalizeButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await LoginAsync(Page, applicantEmail, password);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return (appId, applicantEmail, password);
    }
}

[SetUpFixture]
public class GlobalTeardown
{
    [OneTimeTearDown]
    public void RunAfterAllTests()
    {
        // Aspire host is cleaned up when the process exits.
        // We don't dispose mid-run because the fixture is shared across test classes.
    }
}
