using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Applications;

/// <summary>
/// Spec 029 / US6 (T026) — the application-create Group/Fund anchor. The E2E
/// fixture assigns every fresh applicant to all seeded groups (Norte/Sur/Centro,
/// all under the Active "Migración inicial" Process + "Fondo General" Fund), so
/// the applicant has ≥2 eligible groups and the create form renders the required
/// Process/convocatoria selector (FR-018, ≥2 → required choice). Validates that
/// the selector is shown, blocks submission until chosen, and that a chosen
/// anchor produces a draft that opens in the editor.
/// </summary>
public class ApplicationFundAnchorTests : AuthenticatedTestBase
{
    [Test]
    public async Task Create_WithMultipleEligibleGroups_RendersRequiredSelector_AndAnchorsOnChoice()
    {
        var u = Guid.NewGuid().ToString("N")[..8];
        var email = $"anchor_{u}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Anchor", "Applicant", $"ANC-{u}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        // ≥2 eligible groups → the required <select> is rendered (not a hidden input).
        var groupSelect = Page.Locator("select[data-testid=application-create-group]");
        await Expect(groupSelect).ToBeVisibleAsync();

        // Choose a Process/convocatoria + company name → the draft is created and
        // anchored, opening the editor (FR-018 happy path). The server-side block
        // when no group is chosen is covered by unit + integration tests.
        await appPage.CompanyNameInput.FillAsync($"Empresa {u}");
        await groupSelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
    }
}
