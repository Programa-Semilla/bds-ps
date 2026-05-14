// Spec 021 / US8 / T151 / FR-021 / SC-011 — E2E regression for the meeting-PDF
// screenshot defect: an admin soft-deletes a draft Application and the
// applicant's dashboard still surfaces it as an active draft with the
// "borrador listo para enviar" prompt.
//
// The flow drives the real user journey:
//   1. Applicant signs in, creates a draft Application.
//   2. Reload the applicant home (/) — assert it shows 1 active solicitud and
//      the "borrador" awaiting-action prompt for the new draft.
//   3. Admin soft-deletes the draft via the dev-only
//      /Account/SoftDeleteApplication helper (the production route is the
//      admin-only POST /Admin/Applications/{id}/SoftDelete, which calls
//      Application.SoftDelete() under the hood — same code path).
//   4. Applicant reloads / — assert the counter is now 0 and the prompt is
//      gone (FR-021 / SC-011).
//   5. /Application list also no longer shows the row.
//
// Soft-delete is a structural read-path bug fix; the regression must
// reproduce the original symptom if the predicate is bypassed and stay
// green once every read path is routed through IApplicationQueryFilter.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

[TestFixture]
public class US8_DeletedNotActive : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    [Test]
    public async Task SoftDeletedDraft_DisappearsFromApplicantDashboardAndList()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var applicantEmail = $"us8_app_{unique}@example.com";

        // ----- 1. Register applicant + create a draft Application -----
        await RegisterUserAsync(Page, applicantEmail, Password, "Soft", "Deleted", $"USDA-{unique}");
        await LoginAsync(Page, applicantEmail, Password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync($"Empresa US8 {unique}");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Details/\d+"));

        var idMatch = Regex.Match(Page.Url, @"/Application/Details/(\d+)");
        Assert.That(idMatch.Success, Is.True, "Could not parse Application Id from URL.");
        var applicationId = int.Parse(idMatch.Groups[1].Value);

        // ----- 2. Reload applicant home — assert active counter + prompt -----
        await Page.GotoAsync($"{BaseUrl}/");

        var dashboard = Page.Locator("[data-testid=\"applicant-dashboard\"]");
        await Expect(dashboard).ToBeVisibleAsync();

        var activeKpi = Page.Locator("[data-testid=\"kpi-active\"] .fl-kpi-value");
        await Expect(activeKpi).ToContainTextAsync("1");

        // FR-021 — the "Su borrador para … está listo para enviar." awaiting-action
        // prompt is the exact phrasing surfaced on the meeting-PDF screenshot.
        var awaitingAction = Page.Locator("[data-testid=\"awaiting-action\"]");
        await Expect(awaitingAction).ToBeVisibleAsync();
        await Expect(awaitingAction).ToContainTextAsync("borrador");

        // ----- 3. Soft-delete via the dev-only helper. The route invokes
        //          Application.SoftDelete() so the data path matches the
        //          production POST /Admin/Applications/{id}/SoftDelete. -----
        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) })
        {
            var resp = await client.GetAsync(
                $"/Account/SoftDeleteApplication?applicationId={applicationId}");
            resp.EnsureSuccessStatusCode();
        }

        // ----- 4. Reload applicant home — assert counter = 0 + no prompt -----
        await Page.GotoAsync($"{BaseUrl}/");

        // After soft-delete the applicant might land on the empty state (count
        // 0 in every KPI) OR the dashboard with all KPI tiles reading 0.
        // Either layout satisfies the SC-011 invariant: no draft prompt.
        var emptyState = Page.Locator("[data-testid=\"applicant-empty\"]");
        var dashboardAfter = Page.Locator("[data-testid=\"applicant-dashboard\"]");
        // Wait for one of the two layouts to be in the DOM before asserting.
        await Expect(emptyState.Or(dashboardAfter)).ToBeVisibleAsync();

        var dashboardVisible = await dashboardAfter.IsVisibleAsync();
        if (dashboardVisible)
        {
            var activeAfter = Page.Locator("[data-testid=\"kpi-active\"] .fl-kpi-value");
            await Expect(activeAfter).ToContainTextAsync("0");
        }

        // FR-021 / SC-011 — the awaiting-action prompt is gone. (When the
        // empty-state layout renders, the prompt's data-testid is not
        // attached to any element at all, so Not.ToBeVisibleAsync still holds.)
        await Expect(Page.Locator("[data-testid=\"awaiting-action\"]"))
            .Not.ToBeVisibleAsync();

        // ----- 5. /Application list view also drops the deleted row. -----
        await Page.GotoAsync($"{BaseUrl}/Application");

        // The list view renders either an empty-state region or a list of
        // ApplicationListItem rows. The deleted row's PublicCode/CompanyName
        // must not appear in either case.
        await Expect(Page.Locator($"text=Empresa US8 {unique}"))
            .Not.ToBeVisibleAsync();
    }
}
