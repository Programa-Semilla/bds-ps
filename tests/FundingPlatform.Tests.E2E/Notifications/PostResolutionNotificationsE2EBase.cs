using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 028 — shared journey base for the post-resolution notification E2E tests.
/// Unlike the existing seed helpers (which register users at <c>@example.com</c> and
/// therefore have their mail dropped by the dev allowlist), every user here is
/// registered at <c>@programa-semilla.test</c> so smtp4dev captures the mail
/// (CLAUDE.md / FR-017). Drives the real UI from application creation through
/// reviewer finalize → <c>Resolved</c>, which is the precondition for all 12 events.
/// </summary>
public abstract class PostResolutionNotificationsE2EBase : AuthenticatedTestBase
{
    protected const string Password = "Test123!";
    protected string Quotation = string.Empty;
    protected string UniqueId = string.Empty;
    protected string ApplicantEmail = string.Empty;
    protected string ReviewerEmail = string.Empty;

    [SetUp]
    public void SetUpBase()
    {
        UniqueId = Guid.NewGuid().ToString("N")[..8];
        Quotation = Path.Combine(Path.GetTempPath(), $"pr-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(Quotation, "Test quotation document content");
    }

    [TearDown]
    public async Task TearDownBase()
    {
        if (File.Exists(Quotation)) File.Delete(Quotation);
        if (MailCapture is not null) await MailCapture.DrainAsync();
    }

    protected async Task LogoutAsync() =>
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

    /// <summary>
    /// Registers an allowlisted applicant + a group reviewer, builds a one-item
    /// application with two supplier quotations, submits it, and has the reviewer
    /// approve (or reject) the item and finalize the review → the application is
    /// <c>Resolved</c>. Leaves the session logged out. Stores the applicant +
    /// reviewer emails on the protected fields.
    /// </summary>
    protected async Task<(int AppId, int ItemId)> DriveToResolvedAsync(bool rejectItem)
    {
        ApplicantEmail = $"pr_app_{UniqueId}@programa-semilla.test";
        ReviewerEmail = $"pr_rev_{UniqueId}@programa-semilla.test";

        await RegisterUserAsync(Page, ApplicantEmail, Password, "Tina", "Solicitante", $"PRA-{UniqueId}");
        await LoginAsync(Page, ApplicantEmail, Password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Convenio Item", 0, "Specs", BaseUrl);

        var supplierPage = new SupplierPage(Page);
        var addSupplier = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplier.ClickAsync();
        await supplierPage.FillSupplierFormAsync(
            IdentificationData.CedulaJuridica($"PR1-{UniqueId}"), "Proveedor Alfa", 900m, "2027-12-31", Quotation);
        await supplierPage.SubmitAsync();

        addSupplier = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplier.ClickAsync();
        await supplierPage.FillSupplierFormAsync(
            IdentificationData.CedulaJuridica($"PR2-{UniqueId}"), "Proveedor Beta", 1100m, "2027-12-31", Quotation);
        await supplierPage.SubmitAsync();

        await SetImpactFromEditAsync(appId);
        await SubmitDraftViaReviewAsync(appId);
        await Expect(Page.Locator($"[data-testid=status-pill]:has-text('{UiCopy.State.Submitted}')")).ToBeVisibleAsync();
        await LogoutAsync();

        await RegisterUserAsync(Page, ReviewerEmail, Password, "Rita", "Revisora", $"PRR-{UniqueId}");
        await AssignRoleAsync(ReviewerEmail, "Reviewer");
        await LoginAsync(Page, ReviewerEmail, Password);

        var reviewPage = new ReviewApplicationPage(Page);
        await reviewPage.GotoAsync(BaseUrl, appId);
        var firstItem = reviewPage.ItemCards.First;
        var itemId = int.Parse((await firstItem.GetAttributeAsync("data-item-id"))!);

        if (rejectItem)
        {
            await reviewPage.ItemDecisionRadio(itemId, "Reject").CheckAsync();
            await reviewPage.ItemCommentField(itemId).FillAsync("Ítem rechazado para la prueba");
        }
        else
        {
            await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
            var dropdown = reviewPage.ItemSupplierDropdown(itemId);
            var opts = await dropdown.Locator("option").AllAsync();
            await dropdown.SelectOptionAsync(await opts[1].GetAttributeAsync("value") ?? "");
        }
        await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
        await Expect(Page.Locator(".alert-success")).ToBeVisibleAsync();

        await reviewPage.FinalizeButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
        await LogoutAsync();

        return (appId, itemId);
    }
}
