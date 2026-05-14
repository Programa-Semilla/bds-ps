// Spec 021 / US7 / T141 / FR-029 / FR-030 / FR-031 / SC-012 / SC-015 — E2E
// coverage for the acompañamiento copy pivot + public landing scaffold.
//
// Three flows:
//   1. Anonymous visit to `/` → hero CTA (¿Listo para acelerar tu negocio?) +
//      button (Iniciar acompañamiento) + 3 slot regions (Reglamento, Ejemplo,
//      Sponsor strip). Slots without uploaded files render *Próximamente*.
//   2. Authenticated visit as Vivi → applicant dashboard greeting reads
//      "Hola, Vivi" (FR-030). Anonymous landing is NOT shown to signed-in
//      users — the controller redirects per-role.
//   3. ForbiddenStringsCrawler sweep across every applicant-facing surface →
//      zero `/financiamiento/i` (FR-029, SC-012) and zero `/Bienvenido\/?a/i`
//      (FR-030, SC-015). /FundingAgreement/* surfaces are exempt (legal
//      carve-out — FR-029).

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

[TestFixture]
public class US7_AcompanamientoCopyAndLanding : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    [Test]
    public async Task PublicLanding_RendersHeroCtaAndSlotsAndSponsorStrip_WhenAnonymous()
    {
        var landing = new PublicLandingPage(Page);
        await landing.GotoAsync(BaseUrl);

        // FR-029 hero CTA + button copy (resx-mirrored).
        await Expect(landing.Hero).ToBeVisibleAsync();
        await Expect(landing.Cta).ToContainTextAsync("¿Listo para acelerar tu negocio?");
        await Expect(landing.CtaButton).ToContainTextAsync("Iniciar acompañamiento");

        // FR-031 three slot regions are rendered (Reglamento, Ejemplo, Sponsor strip).
        await Expect(landing.ReglamentoSlot).ToBeVisibleAsync();
        await Expect(landing.EjemploSlot).ToBeVisibleAsync();
        await Expect(landing.SponsorStrip).ToBeVisibleAsync();

        // No files uploaded yet — both slot cards expose the *Próximamente*
        // placeholder rather than a download link.
        await Expect(landing.ReglamentoPlaceholder).ToBeVisibleAsync();
        await Expect(landing.ReglamentoPlaceholder).ToContainTextAsync("Próximamente");
        await Expect(landing.EjemploPlaceholder).ToBeVisibleAsync();
        await Expect(landing.EjemploPlaceholder).ToContainTextAsync("Próximamente");

        await Expect(landing.ReglamentoLink).Not.ToBeVisibleAsync();
        await Expect(landing.EjemploLink).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task PublicLandingDownloads_404_WhenSlotUnconfigured()
    {
        // FR-031 — when no admin has uploaded a slot file, a direct hit to the
        // download URL must 404 (never expose stub bytes or leak storage state).
        var response = await Page.GotoAsync($"{BaseUrl}/files/public-landing/reglamento");
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(404));

        response = await Page.GotoAsync($"{BaseUrl}/files/public-landing/ejemplo");
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task ApplicantDashboard_GreetsWithHolaName_WhenLoggedIn()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var applicantEmail = $"us7_vivi_{unique}@example.com";

        // FR-030 — "Hola, {Nombre}" greeting (Vivi is the first name).
        await RegisterUserAsync(Page, applicantEmail, Password, "Vivi", "Test", $"VIVI-{unique}");
        await LoginAsync(Page, applicantEmail, Password);

        // Authenticated landing → applicant dashboard (HomeController redirects
        // by role; default Applicant falls through to ApplicantDashboard view).
        await Page.GotoAsync($"{BaseUrl}/");

        var greeting = Page.Locator("[data-testid=\"welcome-headline\"]");
        await Expect(greeting).ToBeVisibleAsync();
        await Expect(greeting).ToContainTextAsync("Hola, Vivi");
    }

    [Test]
    public async Task ForbiddenStrings_AreAbsentFromApplicantFacingSurfaces()
    {
        // Seed an applicant + draft so the crawler can visit
        // Application/Details (and the async-loaded panel partial flowing into it).
        var unique = Guid.NewGuid().ToString("N")[..6];
        var applicantEmail = $"us7_crawl_{unique}@example.com";
        await RegisterUserAsync(Page, applicantEmail, Password, "Vivi", "Crawler", $"CRAW-{unique}");
        await LoginAsync(Page, applicantEmail, Password);

        // SC-012 / SC-015 — every applicant-facing surface MUST be free of
        // `/financiamiento/i` and `/Bienvenido\/?a/i` matches. The crawler
        // exempts /FundingAgreement/* URLs (legal carve-out — FR-029 keeps
        // the term on the Funding Agreement document surface).
        //
        // Routes are intentionally narrow: only surfaces the applicant role
        // is authorised to reach. Reviewer / admin queues are not "applicant-
        // facing" and are not crawled here. The dashboard is reached via the
        // `/` redirect path; /Application is the list view; /Account/Login is
        // anonymous; /Profile is the applicant self-service surface.
        var routes = new[]
        {
            "/",
            "/Application",
            "/Profile",
        };

        var crawler = new ForbiddenStringsCrawler(
            Page,
            BaseUrl,
            routes,
            carveOutSubstrings: new[] { "/FundingAgreement", "/funding-agreement" });

        await crawler.AssertNoMatchesAsync(new[]
        {
            new Regex("financiamiento", RegexOptions.IgnoreCase),
            new Regex(@"Bienvenido\/?a", RegexOptions.IgnoreCase),
        });

        // Spot-check the anonymous landing as well (no auth required). The
        // ForbiddenStringsCrawler does not log out between routes; this
        // anonymous sweep runs on a clean context.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        var anonCrawler = new ForbiddenStringsCrawler(
            Page,
            BaseUrl,
            new[] { "/", "/Account/Login", "/Account/Register" },
            carveOutSubstrings: new[] { "/FundingAgreement", "/funding-agreement" });

        await anonCrawler.AssertNoMatchesAsync(new[]
        {
            new Regex("financiamiento", RegexOptions.IgnoreCase),
            new Regex(@"Bienvenido\/?a", RegexOptions.IgnoreCase),
        });
    }
}
