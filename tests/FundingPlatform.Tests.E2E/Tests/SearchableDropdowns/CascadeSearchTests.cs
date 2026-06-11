// Spec 031 / US3 (T025) — searchable cascading controls.
//
// Two surfaces:
//  1. Location cascade (_LocationCascade): picking a Provincia (San José) AJAX-loads
//     >7 cantones, so the Cantón level becomes a searchable combobox. We assert the
//     newly-loaded options are filterable (FR-008) and that matching is accent-
//     insensitive ("perez" → "Pérez Zeledón", FR-002).
//  2. Group drilldown (_GroupSelectorDrilldown): the group checkbox list gets an
//     in-place text filter that narrows the visible groups while checked groups keep
//     accumulating as chips across filter changes (FR-007, spec-016/029 preserved).

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.SearchableDropdowns;

public class CascadeSearchTests : AuthenticatedTestBase
{
    private const string Password = "Test123!";

    // ---- US3 part 1: location cascade cantón search ----

    [Test]
    public async Task LocationCascade_CantonLevel_IsSearchableAfterProvincePick()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"s031us3_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, Password, "Loc", "Tester", $"L31-{uniqueId}");
        await LoginAsync(Page, email, Password);

        // Applicant journey to the supplier-add form (renders _LocationCascade).
        await Page.GotoAsync($"{BaseUrl}/Application");
        await Page.Locator("a:has-text('Iniciar acompañamiento')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));

        var appPage = new ApplicationPage(Page);
        await appPage.CompanyNameInput.FillAsync($"Cascada {uniqueId}");
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var draft = new ApplicationDraftPage(Page);
        await draft.ImpactLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/\d+/Impact"));
        await CompleteImpactStepAsync();
        await draft.AddItemAsync("Horno industrial", "Acero inoxidable, 60L");
        await Expect(draft.ItemRows).ToHaveCountAsync(1);

        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Supplier/Add"));

        var supplier = new SupplierPage(Page);
        var legalId = IdentificationData.CedulaJuridica($"L31-{uniqueId}");
        Assert.That(await supplier.SearchByLegalIdAsync(legalId), Is.EqualTo("Empty"));

        // Province select is server-rendered (7 options → stays plain). Pick San José
        // (20 cantones) and wait for them to load.
        var province = Page.Locator("[data-testid=\"province-NewSupplier-FirstBranch-ProvinceId\"]");
        var cantonsResp = Page.WaitForResponseAsync(r => r.Url.Contains("/api/cantons"));
        await province.SelectOptionAsync(new SelectOptionValue { Label = "San José" });
        await cantonsResp;

        // The Cantón level now has >7 options → it enhances into a combobox.
        var canton = new SearchableSelect(Page, "canton-NewSupplier-FirstBranch-CantonId");
        await Expect(canton.Input).ToBeVisibleAsync();

        // Accent-insensitive filter over the freshly-loaded cantones (FR-002/FR-008):
        // "perez" matches "Pérez Zeledón" despite the missing accent.
        await canton.FilterAsync("perez");
        await Expect(canton.Options).ToHaveCountAsync(1);
        await Expect(canton.Options.First).ToContainTextAsync("Pérez Zeledón");

        // Commit narrows correctly and sets the native value.
        await canton.SelectSearchableAsync("Pérez Zeledón");
        Assert.That(await canton.CommittedValueAsync(), Is.Not.Empty);
    }

    // ---- US3 part 2: group drilldown checkbox filter ----

    [Test]
    public async Task GroupDrilldown_Filter_NarrowsGroupsAndPreservesAccumulation()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"s031us3admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "S031", "Admin", $"L31A-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        await Page.GotoAsync($"{BaseUrl}/Admin/Users/Create");
        // Drilldown JS finished building fund options.
        await Page.Locator("[data-testid=\"group-drilldown-selector\"][data-ready=\"true\"]").WaitForAsync();

        // Drive the fund/process via the native selects (works whether or not they
        // enhanced) to render the seeded group checkboxes (Norte/Sur/Centro).
        await Page.Locator("[data-testid=\"group-selector-fund\"]")
            .SelectOptionAsync(new SelectOptionValue { Label = "Fondo General" });
        await Page.Locator("[data-testid=\"group-selector-process\"]")
            .SelectOptionAsync(new SelectOptionValue { Label = "Migración inicial" });

        var options = Page.Locator("[data-testid=\"group-selector-options\"]");
        var filter = Page.Locator("[data-testid=\"group-selector-filter\"]");
        var norte = options.Locator("label.form-check").Filter(new LocatorFilterOptions { HasText = "Norte" });
        var sur = options.Locator("label.form-check").Filter(new LocatorFilterOptions { HasText = "Sur" });
        var centro = options.Locator("label.form-check").Filter(new LocatorFilterOptions { HasText = "Centro" });

        await Expect(filter).ToBeVisibleAsync();
        await Expect(norte).ToBeVisibleAsync();

        // Filter "Nor" → only Norte remains visible.
        await filter.FillAsync("Nor");
        await Expect(norte).ToBeVisibleAsync();
        await Expect(sur).Not.ToBeVisibleAsync();
        await Expect(centro).Not.ToBeVisibleAsync();

        // Check Norte while filtered.
        await norte.Locator("input[type=checkbox]").CheckAsync();
        await Expect(Page.Locator("[data-testid=\"group-selector-chips\"]")).ToContainTextAsync("Norte");

        // Change the filter to "Cen": Norte hides but stays checked; check Centro too.
        await filter.FillAsync("Cen");
        await Expect(centro).ToBeVisibleAsync();
        await Expect(norte).Not.ToBeVisibleAsync();
        await centro.Locator("input[type=checkbox]").CheckAsync();

        // Clear the filter — both groups are visible again, both checked, both chipped.
        await filter.FillAsync("");
        await Expect(norte).ToBeVisibleAsync();
        await Expect(centro).ToBeVisibleAsync();
        await Expect(norte.Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await Expect(centro.Locator("input[type=checkbox]")).ToBeCheckedAsync();
        var chips = Page.Locator("[data-testid=\"group-selector-chips\"]");
        await Expect(chips).ToContainTextAsync("Norte");
        await Expect(chips).ToContainTextAsync("Centro");
    }
}
