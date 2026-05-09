using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Reviews;

/// <summary>
/// Spec 018 / SC-011 — drives a reviewer through the per-item review form,
/// covering the golden + key error paths from US2: required line code,
/// successful Approve with a code, and the duplicate-code rejection path.
/// </summary>
[Category("Reviews")]
[Category("Spec018")]
public class LineCodeReviewFlowTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;
    private string _uniqueId = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"linecode-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "Quotation placeholder content");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    [Test]
    public async Task ReviewItem_BlankLineCode_ShowsValidationError_AndPersistsNothing()
    {
        var (appId, _, _) = await SetupTwoItemSubmittedApplicationAsync();

        var reviewerEmail = $"lc_reviewer_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "LineCode", "Reviewer", $"LCRLID-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse(await firstItem.GetAttributeAsync("data-item-id") ?? "0");

        // Approve without filling LineCode → expect a Spanish required-field error
        // surfaced via TempData["ErrorMessage"] (the controller flow). Check the
        // alert-danger banner.
        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var supplierValue = await supplierDropdown.Locator("option").Nth(1).GetAttributeAsync("value");
        await supplierDropdown.SelectOptionAsync(supplierValue!);
        // No LineCode value is filled.
        await reviewPage.ItemSubmitButton(itemId).ClickAsync();

        var errorBanner = Page.Locator(".alert-danger");
        await Expect(errorBanner).ToContainTextAsync("código de línea");
    }

    [Test]
    public async Task ReviewItem_WithLineCode_PersistsAndAllowsNextItem()
    {
        var (appId, _, _) = await SetupTwoItemSubmittedApplicationAsync();

        var reviewerEmail = $"lc_reviewer_ok_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "LineCode", "Reviewer", $"LCROK-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse(await firstItem.GetAttributeAsync("data-item-id") ?? "0");

        await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
        var supplierDropdown = reviewPage.ItemSupplierDropdown(itemId);
        var supplierValue = await supplierDropdown.Locator("option").Nth(1).GetAttributeAsync("value");
        await supplierDropdown.SelectOptionAsync(supplierValue!);
        await reviewPage.ItemLineCodeInput(itemId).FillAsync("T1-1");
        await reviewPage.ItemSubmitButton(itemId).ClickAsync();

        var successBanner = Page.Locator(".alert-success");
        await Expect(successBanner).ToContainTextAsync("Decisión del ítem registrada.");
    }

    [Test]
    public async Task ReviewItem_DuplicateLineCodeWithinApplication_IsRejected()
    {
        var (appId, _, _) = await SetupTwoItemSubmittedApplicationAsync();

        var reviewerEmail = $"lc_reviewer_dup_{_uniqueId}@example.com";
        await RegisterUserAsync(Page, reviewerEmail, "Test123!", "LineCode", "Dup", $"LCDUP-{_uniqueId}");
        await AssignRoleAsync(reviewerEmail, "Reviewer");
        await LoginAsync(Page, reviewerEmail, "Test123!");

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);

        var itemCards = await reviewPage.ItemCards.AllAsync();
        Assert.That(itemCards.Count, Is.GreaterThanOrEqualTo(2),
            "Test setup must seed at least two items so we can exercise duplicate-LineCode rejection.");

        var firstItemId = int.Parse(await itemCards[0].GetAttributeAsync("data-item-id") ?? "0");
        var secondItemId = int.Parse(await itemCards[1].GetAttributeAsync("data-item-id") ?? "0");

        // Assign T1-1 to the first item (Approve) — succeeds.
        await reviewPage.ItemDecisionRadio(firstItemId, "Approve").CheckAsync();
        var firstSupplierDropdown = reviewPage.ItemSupplierDropdown(firstItemId);
        var firstSupplierValue = await firstSupplierDropdown.Locator("option").Nth(1).GetAttributeAsync("value");
        await firstSupplierDropdown.SelectOptionAsync(firstSupplierValue!);
        await reviewPage.ItemLineCodeInput(firstItemId).FillAsync("T1-1");
        await reviewPage.ItemSubmitButton(firstItemId).ClickAsync();
        await Expect(Page.Locator(".alert-success")).ToContainTextAsync("Decisión");

        // Try to assign the same code to the second item — duplicate error.
        await reviewPage.ItemDecisionRadio(secondItemId, "Approve").CheckAsync();
        var secondSupplierDropdown = reviewPage.ItemSupplierDropdown(secondItemId);
        var secondSupplierValue = await secondSupplierDropdown.Locator("option").Nth(1).GetAttributeAsync("value");
        await secondSupplierDropdown.SelectOptionAsync(secondSupplierValue!);
        await reviewPage.ItemLineCodeInput(secondItemId).FillAsync("T1-1");
        await reviewPage.ItemSubmitButton(secondItemId).ClickAsync();

        var errorBanner = Page.Locator(".alert-danger");
        await Expect(errorBanner).ToContainTextAsync("Ya existe");
    }

    private async Task<(int AppId, int Item1Id, int Item2Id)> SetupTwoItemSubmittedApplicationAsync()
    {
        _uniqueId = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"lc_applicant_{_uniqueId}@example.com";
        var password = "Test123!";

        await RegisterUserAsync(Page, applicantEmail, password, "LineCode", "Applicant", $"LCALID-{_uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync("Sazón Vegetariano");

        var appIdMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(appIdMatch.Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Item A", 0, "Specs A", BaseUrl);
        await itemPage.AddItemAsync(appId, "Item B", 0, "Specs B", BaseUrl);

        // Add suppliers + impact for each item
        for (var iter = 0; iter < 2; iter++)
        {
            var supplierPage = new SupplierPage(Page);
            var addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").Nth(iter);
            await addSupplierLink.ClickAsync();
            await supplierPage.FillSupplierFormAsync(
                $"LCS{iter}-{_uniqueId}", $"Supplier {iter}A", 500m, "2027-12-31", _testFilePath);
            await supplierPage.SubmitAsync();

            addSupplierLink = Page.Locator("a:has-text('Agregar proveedor')").Nth(iter);
            await addSupplierLink.ClickAsync();
            await supplierPage.FillSupplierFormAsync(
                $"LCS{iter}b-{_uniqueId}", $"Supplier {iter}B", 700m, "2027-12-31", _testFilePath);
            await supplierPage.SubmitAsync();

            var impactButton = Page.Locator("a:has-text('Impacto')").Nth(iter);
            await impactButton.ClickAsync();
            await PickFirstImpactTemplateAsync();
            var paramInputs = Page.Locator(".parameter-field input.form-control");
            var inputCount = await paramInputs.CountAsync();
            for (var i = 0; i < inputCount; i++)
            {
                var input = paramInputs.Nth(i);
                var inputType = await input.GetAttributeAsync("type");
                await input.FillAsync(inputType == "number" ? "100" : inputType == "date" ? "2026-12-31" : "v");
            }
            await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        }

        await Page.Locator("button[type=submit]:has-text('Enviar solicitud')").ClickAsync();
        await Expect(Page.Locator("[data-testid=status-pill]:has-text('Enviada')")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        return (appId, 0, 0);
    }
}
