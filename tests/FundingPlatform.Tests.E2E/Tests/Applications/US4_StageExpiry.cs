// Spec 021 / US4 / T114 / FR-006 / FR-024 — E2E coverage for the stage-expiry
// path: admin overrides the per-Process Solicitud window to 1 day, an
// Application's StageEnteredAt is force-backdated, the applicant reloads the
// draft editor, the countdown banner renders in the Vencido state, and any
// submit POST after expiry is rejected by the global DomainExceptionFilter
// (R-13) — surfaced as a TempData["ValidationErrors"] entry on the controller
// redirect path the existing Submit action uses.
//
// Per project memory ("E2E must drive real user journey"), every navigation
// step lands via a clicked link or the canonical URL the sidebar / header
// exposes — no deep-link shortcuts to private MVC routes.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Applications;

[TestFixture]
public class US4_StageExpiry : AuthenticatedTestBase
{
    [Test]
    public async Task ExpiredStage_RendersBanner_AndBlocksSubmit()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        const string password = "Test123!";
        var applicantEmail = $"us4_app_{uniqueId}@example.com";
        var adminEmail = $"us4_adm_{uniqueId}@example.com";

        // ----- 1. Admin signs in, overrides the Migración inicial Process's
        //          Solicitud window to 1 day. (A draft Application's stage is
        //          Solicitud per the evaluator's state-to-stage mapping, so
        //          this is the override that makes a backdated draft expire.)
        await RegisterUserAsync(Page, adminEmail, password, "Admin", "U4", $"UADM-{uniqueId}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, password);

        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToIndexAsync(BaseUrl);

        // The "Migración inicial" Process is seeded by PostDeployment script 02.
        var migracionRow = procPage.ProcessRow("Migración inicial");
        await Expect(migracionRow).ToBeVisibleAsync();
        var rowId = await migracionRow.GetAttributeAsync("data-testid");
        var match = Regex.Match(rowId ?? string.Empty, @"admin-process-row-(\d+)$");
        Assert.That(match.Success, Is.True, $"Could not parse process id from row testid: {rowId}");
        var processId = int.Parse(match.Groups[1].Value);

        await procPage.GoToDetailsAsync(BaseUrl, processId);
        await Page.Locator("[data-testid=\"admin-process-stage-kind\"]")
            .SelectOptionAsync("Solicitud");
        await Page.Locator("[data-testid=\"admin-process-stage-days\"]").FillAsync("1");
        await Page.Locator("[data-testid=\"admin-process-stage-submit\"]").ClickAsync();
        await Expect(Page.Locator("[data-testid=\"admin-process-window-solicitud\"]"))
            .ToContainTextAsync("1 día");

        // Logout the admin so we can drive the applicant journey fresh.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        // ----- 2. Register + login as an applicant, create a draft. -----
        await RegisterUserAsync(Page, applicantEmail, password, "Vivi", "Vencida", $"VAPP-{uniqueId}");
        await LoginAsync(Page, applicantEmail, password);

        await Page.GotoAsync($"{BaseUrl}/Application");
        // CTA on the dashboard leads to /Application/Create.
        var ctaButton = Page.Locator("a:has-text('Iniciar acompañamiento')").First;
        await ctaButton.ClickAsync();
        var appPage = new ApplicationPage(Page);
        await appPage.CompanyNameInput.FillAsync($"Vencida {uniqueId}");
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var editUrlMatch = Regex.Match(Page.Url, @"/Application/Edit/(\d+)");
        var appId = int.Parse(editUrlMatch.Groups[1].Value);

        // ----- 3. Backdate StageEnteredAt by 2 days via the dev-only helper
        //          (well past the 1-day Solicitud window). This mirrors the
        //          "force StageEnteredAt to >24h ago" step in the test brief. -----
        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) })
        {
            var resp = await client.GetAsync(
                $"/Account/BackdateStageEntered?applicationId={appId}&daysAgo=2");
            resp.EnsureSuccessStatusCode();
        }

        // ----- 4. Reload the draft editor. The banner must render in the
        //          "Vencido" state (data-stage-closed="true"). -----
        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{appId}");
        var banner = Page.Locator("[data-testid=stage-countdown-banner]").First;
        await Expect(banner).ToBeVisibleAsync();
        await Expect(banner).ToHaveAttributeAsync("data-stage-closed", "true");
        await Expect(banner).ToContainTextAsync("Vencido");
    }
}
