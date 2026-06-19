using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Manual;

/// <summary>
/// Documentation utility (NOT a product assertion). Drives the real applicant
/// journey end-to-end and writes one screenshot per key screen to
/// <c>docs/manual/img/applicant/</c> so the Solicitante user manual can embed
/// real captures. Run on demand:
///   dotnet test tests/FundingPlatform.Tests.E2E \
///     --filter "FullyQualifiedName~ManualScreenshotsCaptureTests"
/// Reuses the shared AspireFixture, dev seams, page objects, and the
/// FundingAgreementSeeder exactly as the functional E2E suite does.
/// </summary>
public class ManualScreenshotsCaptureTests : AuthenticatedTestBase
{
    private string OutDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "docs", "manual", "img", "applicant"));

    private async Task ShotAsync(string fileName)
    {
        Directory.CreateDirectory(OutDir);
        var bytes = await Page.ScreenshotAsync(new() { FullPage = true });
        await File.WriteAllBytesAsync(Path.Combine(OutDir, fileName), bytes);
        TestContext.WriteLine($"[manual-shot] {fileName}");
    }

    [Test]
    public async Task CaptureApplicantManual()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"manual_app_{uid}@example.com";
        var reviewerEmail = $"manual_rev_{uid}@example.com";

        var pdfPath = Path.Combine(Path.GetTempPath(), $"manual-quote-{uid}.pdf");
        await File.WriteAllBytesAsync(pdfPath,
            System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\nmanual quote\n%%EOF\n"));

        await Page.SetViewportSizeAsync(1280, 900);

        // --- 01 Login (unauthenticated) -------------------------------------
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await ShotAsync("01-login.png");

        // --- 02 Forgot password ---------------------------------------------
        await Page.GotoAsync($"{BaseUrl}/Account/ForgotPassword");
        await ShotAsync("02-recuperar-contrasena.png");

        // Seed + sign in the applicant (dev seam; assigns all seeded groups so
        // the create form renders the group selector).
        await RegisterUserAsync(Page, applicantEmail, password, "María", "Rodríguez Solano", $"MANA-{uid}");
        await LoginAsync(Page, applicantEmail, password);

        // --- 03 Applicant home (empty list) ---------------------------------
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await ShotAsync("03-inicio.png");

        // --- 04 Create application form --------------------------------------
        await appPage.CreateButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Create"));
        await appPage.CompanyNameInput.FillAsync("Cooperativa Verde R.L.");
        await appPage.SelectEligibleGroupIfPresentAsync();
        await ShotAsync("04-crear-solicitud.png");
        await appPage.SubmitDraftButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        // --- 05 Draft editor (empty) ----------------------------------------
        await ShotAsync("05-borrador-vacio.png");

        // --- 06 Declare an application-level impact --------------------------
        var impacts = new ApplicationImpactsPage(Page);
        await impacts.GotoAsync(appId, BaseUrl);
        await impacts.AddImpactAsync(0);
        await ShotAsync("06-impactos.png");

        // --- 07 Add a line item (category fields + impact attribution) -------
        var itemPage = new ItemPage(Page);
        await Page.GotoAsync($"{BaseUrl}/Application/{appId}/Item/Add");
        await itemPage.SelectCategoryAndFillFieldsAsync(0);
        await itemPage.ProductNameInput.FillAsync("Compresor de aire 50L");
        await itemPage.AttributeFirstImpactAndJustifyAsync(
            "Este equipo aumenta la capacidad de producción del taller.");
        await ShotAsync("07-agregar-item.png");
        await itemPage.SubmitButton.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));

        // --- 08 Add a supplier + quotation (filled form) --------------------
        var supplier = new SupplierPage(Page);
        await Page.Locator("a[href*='/Supplier/Add']").First.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Supplier/Add"));
        await supplier.FillSupplierFormAsync(
            IdentificationData.CedulaJuridica($"S1-{uid}"), "Equipos Industriales S.A.",
            850000m, "2027-12-31", pdfPath);
        await ShotAsync("08-agregar-proveedor.png");
        await supplier.SubmitAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Second quotation (min 2 to submit) — no capture.
        await Page.Locator("a[href*='/Supplier/Add']").First.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/Supplier/Add"));
        await supplier.FillSupplierFormAsync(
            IdentificationData.CedulaJuridica($"S2-{uid}"), "Maquinaria del Valle",
            910000m, "2027-12-31", pdfPath);
        await supplier.SubmitAsync();
        await Page.WaitForURLAsync(new Regex(@"/Application/Edit/\d+"));

        // --- 09 Draft editor (complete) -------------------------------------
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        await ShotAsync("09-borrador-completo.png");

        // --- 10 Review surface ----------------------------------------------
        var submit = Page.Locator("[data-testid=application-edit-submit]");
        await Expect(submit).ToBeEnabledAsync();
        await submit.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/.+/Review"));
        await ShotAsync("10-revision.png");

        // --- 11 Details after submit (Enviada) ------------------------------
        await Page.Locator("[data-testid=review-confirm-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        await ShotAsync("11-detalle-enviada.png");

        // --- 12 Profile ------------------------------------------------------
        await Page.GotoAsync($"{BaseUrl}/Profile");
        await ShotAsync("12-perfil.png");

        // ----- Post-submission states (reviewer-driven). Guarded so a hiccup
        // here cannot discard screens 01–12 already on disk. -----------------
        try
        {
            await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

            // Reviewer finalizes the review so the applicant gets an actionable
            // "Resuelta" response screen.
            await RegisterUserAsync(Page, reviewerEmail, password, "Carlos", "Jiménez", $"MANR-{uid}");
            await AssignRoleAsync(reviewerEmail, "Reviewer");
            await LoginAsync(Page, reviewerEmail, password);

            var reviewPage = new ReviewApplicationPage(Page);
            await reviewPage.GotoAsync(BaseUrl, appId);
            var itemId = int.Parse((await reviewPage.ItemCards.First.GetAttributeAsync("data-item-id"))!);
            await reviewPage.ItemDecisionRadio(itemId, "Approve").CheckAsync();
            var dropdown = reviewPage.ItemSupplierDropdown(itemId);
            var opts = await dropdown.Locator("option").AllAsync();
            await dropdown.SelectOptionAsync(await opts[1].GetAttributeAsync("value") ?? "");
            await reviewPage.SubmitDecisionWithTestLineCodeAsync(itemId);
            await Expect(reviewPage.SuccessMessage).ToBeVisibleAsync();
            await reviewPage.FinalizeButton.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Review"));
            await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

            // --- 13 Applicant responds to the resolution --------------------
            await LoginAsync(Page, applicantEmail, password);
            var responsePage = new ApplicantResponsePage(Page);
            await responsePage.GotoAsync(BaseUrl, appId);
            await ShotAsync("13-responder-revision.png");
            await responsePage.AcceptRadio(itemId).CheckAsync();
            await responsePage.SubmitAsync();
            await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();

            // --- 14 Funding agreement (executed) ----------------------------
            await FundingAgreementSeeder.SeedExecutedAgreementAsync(
                ConnectionString, appId, reviewerEmail, applicantEmail, reviewerEmail,
                CreateBlobServiceClient());
            await Page.GotoAsync($"{BaseUrl}/Applications/{appId}/FundingAgreement");
            await ShotAsync("14-convenio.png");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[manual-shot] post-submission capture skipped: {ex.Message}");
        }
    }
}
