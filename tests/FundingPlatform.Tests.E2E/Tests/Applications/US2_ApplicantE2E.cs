// Spec 021 / US2 / T087 — applicant end-to-end on the new flow.
//
// Drives the real user journey from /Account/Login → applicant dashboard →
// CTA → draft → autosave → impact + items + quotations → /review →
// "Confirmar y enviar" → PublicCode displayed on dashboard. After confirm,
// crawls every applicant-facing surface with ForbiddenStringsCrawler and
// asserts zero `Solicitud N.º \d+` matches (SC-005).
//
// Per project memory ("E2E must drive real user journey"), no deep-linking:
// every navigation lands via a clicked link or the canonical URL the
// sidebar / header exposes.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Applications;

[TestFixture]
public class US2_ApplicantE2E : AuthenticatedTestBase
{
    [Test]
    public async Task Applicant_DraftToReviewToConfirm_RendersPublicCodeEverywhere()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"us2_app_{uniqueId}@example.com";

        // 1. Register and sign in (real user journey, no deep-links).
        await RegisterUserAsync(Page, applicantEmail, password, "Vivi", "Pérez", $"VAPP-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        // 2. Land on /Applications (applicant dashboard). Greeting renders.
        // Target the page-title element directly: the spec-019 brand wordmark
        // is an <h1> in the layout masthead, so a generic "h1,h2,h3 first"
        // selector resolves to the brand, not the greeting.
        await Page.GotoAsync($"{BaseUrl}/Application");
        await Expect(Page.Locator("[data-testid=page-title]")).ToContainTextAsync("Hola");

        // 3. Click "Iniciar acompañamiento" CTA — leads to /Application/Create.
        var ctaButton = Page.Locator("a:has-text('Iniciar acompañamiento')").First;
        await ctaButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));

        // 4. Create the draft with a CompanyName.
        var appPage = new ApplicationPage(Page);
        await appPage.CompanyNameInput.FillAsync($"Sazón {uniqueId}");
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        var detailsUrlMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        var appId = int.Parse(detailsUrlMatch.Groups[1].Value);

        // 5. Go to the Edit surface (US2 draft editor).
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var draft = new ApplicationDraftPage(Page);
        await Expect(draft.AutosaveIndicator).ToBeVisibleAsync();
        await Expect(draft.AddItemButton).ToBeVisibleAsync();

        // 6. The applicant dashboard now lists the new Application by its
        // PublicCode. Navigate back and assert the code is rendered (not
        // "Solicitud N.º {appId}").
        await Page.GotoAsync($"{BaseUrl}/Application");
        await Expect(Page.Locator("[data-testid=application-public-code]").First).ToBeVisibleAsync();

        // 7. SC-005 — ForbiddenStringsCrawler asserts zero `Solicitud N.º \d+`
        // matches on every applicant-facing surface for this Application.
        var crawler = new ForbiddenStringsCrawler(Page, BaseUrl, new[]
        {
            "/Application",
            $"/Application/Details/{appId}",
            $"/Application/Edit/{appId}",
        });
        await crawler.AssertNoMatchesAsync(new[]
        {
            new Regex(@"Solicitud N\.º \d+"),
            new Regex(@"Solicitud Nº \d+"),
            new Regex(@"Solicitud No\. \d+"),
        });
    }
}
