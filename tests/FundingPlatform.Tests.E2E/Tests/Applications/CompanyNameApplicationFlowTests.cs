using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Applications;

/// <summary>
/// Spec 018 / SC-012 — drives an applicant through the Create form, asserting
/// that a blank CompanyName triggers a user-facing required-field error and
/// that a non-blank value persists and surfaces in the downstream PDF cover.
/// The PDF cover-page assertion piggy-backs on the funder operator path; we
/// only verify the visible text inside the post-Create draft Details view here
/// to keep the harness fast.
/// </summary>
[Category("Applications")]
[Category("Spec018")]
public class CompanyNameApplicationFlowTests : AuthenticatedTestBase
{
    [Test]
    public async Task Create_BlankCompanyName_ShowsValidationError_AndDoesNotPersist()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"cn_blank_{uniqueId}@example.com";

        await RegisterUserAsync(Page, email, "Test123!", "Blank", "Applicant", $"BLALID-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        // Submit without filling the input.
        await appPage.SubmitDraftButton.ClickAsync();

        // Form should re-render on the same Create URL with a Spanish required
        // error on the CompanyName input.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));
        var error = appPage.CompanyNameError;
        await Expect(error).ToContainTextAsync("nombre de la empresa");

        // No application should be persisted — empty applications list shows
        // the empty-state component (no <table>), confirming the failed
        // submission did not produce a row.
        await appPage.GotoListAsync(BaseUrl);
        await Expect(appPage.ItemRows).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Create_WithCompanyName_PersistsAndShowsOnDetails()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"cn_ok_{uniqueId}@example.com";

        await RegisterUserAsync(Page, email, "Test123!", "Sazón", "Vegetariano", $"OKALID-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync("Sazón Vegetariano");

        // Spec 021 / US2 — draft creation now opens the draft editor.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        // The CompanyName surfaces in TempData["SuccessMessage"] = "Solicitud creada con éxito."
        // and in the FundingAgreement preview later. For SC-012's first half we just
        // confirm the persistence path didn't reject the input.
        var successBanner = Page.Locator(".alert-success");
        await Expect(successBanner).ToContainTextAsync("Solicitud creada con éxito.");

        // Verify the persisted CompanyName surfaces on the read-only Details summary.
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");
        await Expect(Page.Locator("body")).ToContainTextAsync("Sazón Vegetariano");
    }

    [Test]
    public async Task Create_WhitespaceOnly_RejectsAsBlank()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"cn_ws_{uniqueId}@example.com";

        await RegisterUserAsync(Page, email, "Test123!", "Whitespace", "Applicant", $"WSALID-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        // Fill with only whitespace — server-side trim should reject this.
        await appPage.CompanyNameInput.FillAsync("    ");
        await appPage.SubmitDraftButton.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));
        await Expect(appPage.CompanyNameError).ToContainTextAsync("nombre de la empresa");
    }
}
