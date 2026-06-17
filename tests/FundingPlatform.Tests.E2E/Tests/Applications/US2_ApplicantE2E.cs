// Spec 021 / US2 (spec 035 update) — applicant end-to-end on the new flow.
// Drives the real user journey via clicked links (no deep-linking into MVC routes
// the UI never exposes):
//
//   register/login -> dashboard greeting -> "Iniciar acompañamiento" CTA ->
//   create draft -> draft editor -> add line item (category fields + product) ->
//   gate stays CLOSED while impact is pending -> set per-item impact -> autosave
//   on blur -> gate OPENS -> add supplier quotations -> /review -> "Confirmar y
//   enviar" -> PublicCode rendered, zero "Solicitud N.º N".
//
// Spec 035 moved impact from the Application aggregate to each line item, so the
// app-level Impact step is gone; the gate now reflects per-item impact completeness.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Applications;

[TestFixture]
public class US2_ApplicantE2E : AuthenticatedTestBase
{
    private string _quotationFile = string.Empty;

    [SetUp]
    public void SetUpQuotationFile()
    {
        _quotationFile = Path.Combine(Path.GetTempPath(), $"us2-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFile, "Quotation placeholder content");
    }

    [TearDown]
    public void DeleteQuotationFile()
    {
        if (File.Exists(_quotationFile)) File.Delete(_quotationFile);
    }

    [Test]
    public async Task Applicant_PerItemImpact_GatedSubmit_ReviewConfirm()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"us2_app_{uniqueId}@example.com";

        // 1. Register + sign in.
        await RegisterUserAsync(Page, applicantEmail, password, "Vivi", "Pérez", $"VAPP-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        // 2. Applicant dashboard — "Hola, Vivi" greeting (FR-030).
        await Page.GotoAsync($"{BaseUrl}/Application");
        await Expect(Page.Locator("[data-testid=page-title]")).ToContainTextAsync("Hola");

        // 3. "Iniciar acompañamiento" CTA -> /Application/Create.
        await Page.Locator("a:has-text('Iniciar acompañamiento')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));

        // 4. Create the draft — opens straight in the draft editor (US2).
        var appPage = new ApplicationPage(Page);
        await appPage.CompanyNameInput.FillAsync($"Sazón {uniqueId}");
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
        var draft = new ApplicationDraftPage(Page);

        // 5. FR-017 — submit gate is CLOSED before any item exists.
        await Expect(draft.SubmitButton).ToBeDisabledAsync();

        // 6. Spec 035 — add a line item via the category-first form, WITHOUT impact.
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Horno industrial", 0, "Acero inoxidable, 60L", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        await Expect(draft.ItemRows).ToHaveCountAsync(1);

        // 7. Still gated — the item has no impact template yet ("Impacto pendiente").
        await Expect(draft.SubmitButton).ToBeDisabledAsync();

        // 8. FR-016 — editing a field autosaves on blur.
        await draft.FillCompanyNameAsync($"Sazón Cocina {uniqueId}");
        await Expect(draft.AutosaveIndicator).ToHaveAttributeAsync("data-autosave-state", "saved");

        // 9. Spec 035 — set the per-item impact; the gate then OPENS.
        await SetImpactFromEditAsync(appId);
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        await Expect(draft.SubmitButton).ToBeEnabledAsync();

        // 10. Add two supplier quotations to the item. Supplier/Add is linked from
        //     the editor and redirects back to it.
        for (var i = 1; i <= 2; i++)
        {
            await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
            var supplier = new SupplierPage(Page);
            await supplier.FillSupplierFormAsync(
                $"US2Q{i}{uniqueId}", $"Proveedor {i} {uniqueId}", 900m * i, "2027-12-31", _quotationFile);
            await supplier.SubmitAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        }

        // 11. FR-017 — the gated submit routes to /review.
        await draft.GoToReviewAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Applications/.+/Review"));
        await Expect(Page.Locator("[data-testid=review-items-card]")).ToBeVisibleAsync();
        // Spec 035 / D9 — impact renders per line item, not as one application card.
        await Expect(Page.Locator("[data-testid=review-item-row]").First).ToBeVisibleAsync();

        // 12. Confirm and send.
        await Page.Locator("[data-testid=review-confirm-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));
        await Expect(Page.Locator("[data-testid=status-pill]").First)
            .ToContainTextAsync(UiCopy.State.Submitted);

        // 13. SC-005 — the Application surfaces only under its PublicCode.
        await Page.GotoAsync($"{BaseUrl}/Application");
        await Expect(Page.Locator("[data-testid=application-public-code]").First).ToBeVisibleAsync();

        var crawler = new ForbiddenStringsCrawler(Page, BaseUrl, new[]
        {
            "/Application",
            $"/Application/Details/{appId}",
        });
        await crawler.AssertNoMatchesAsync(new[]
        {
            new Regex(@"Solicitud N\.º \d+"),
            new Regex(@"Solicitud Nº \d+"),
            new Regex(@"Solicitud No\. \d+"),
        });
    }
}
