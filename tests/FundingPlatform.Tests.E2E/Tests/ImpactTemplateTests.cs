using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

// Spec 021 / FR-005 — Impact is captured on the Application aggregate upfront,
// on its own step (/Application/{id}/Impact), reached from the draft editor —
// never through the retired per-Item Impact route.
public class ImpactTemplateTests : AuthenticatedTestBase
{
    /// <summary>
    /// Drives Details → "Continuar borrador" → draft editor → "Definir impacto",
    /// landing on the Application-level Impact step.
    /// </summary>
    private async Task<int> CreateDraftAndOpenImpactStepAsync(string emailPrefix)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"{emailPrefix}_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "Impact", "Tester", $"LID-{uniqueId}");
        await LoginAsync(Page, email, password);

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        await Page.Locator("[data-testid=application-edit-impact-link]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/\d+/Impact"));
        return appId;
    }

    [Test]
    public async Task SelectTemplate_And_FillParameters()
    {
        await CreateDraftAndOpenImpactStepAsync("impact_test");

        // Pick the first seeded template, fill every parameter, save —
        // CompleteImpactStepAsync asserts the redirect to /Application/{id}/Edit.
        await CompleteImpactStepAsync();

        // The draft editor's Impact card now reads "Definido".
        await Expect(Page.Locator("[data-testid=application-edit-impact-status]"))
            .ToContainTextAsync("Definido");
    }

    [Test]
    public async Task RequiredParameter_Validation()
    {
        await CreateDraftAndOpenImpactStepAsync("impact_val");

        // Pick a template so the required parameter inputs render.
        await PickFirstImpactTemplateAsync();

        // Do NOT fill the required parameters; attempt to submit.
        await Page.Locator("button[type=submit]:has-text('Guardar impacto')").ClickAsync();

        // The browser's built-in required validation blocks submission — still
        // on the Impact step.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/\d+/Impact"));
    }
}
