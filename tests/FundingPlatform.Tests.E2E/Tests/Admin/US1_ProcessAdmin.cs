// Spec 021 — see specs/021-feedback-session-may13/tasks.md T075 and US1
// acceptance scenarios + SC-002 (snapshot-independence).

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 021 / US1 / T075 — E2E for annual program cycle administration.
/// Drives the real user journey end-to-end:
///   1. admin signs in
///   2. /Admin/Processes empty-state → Create *Crocus 2025*
///   3. /Admin/Plantillas → create *PlantillaMVP-v1* with ≥ 1 ImpactTemplate
///   4. on the Process detail, assign the new Plantilla
///   5. create three Groups (*Norte*, *Sur*, *Centro*) under the Process via
///      the existing /Admin/Groups surface (re-parented to the Process by
///      the Migración inicial seed + the new ProcessId column)
///   6. snapshot-independence: edit the base Plantilla's MinimumQuotations,
///      reopen the Process detail, original snapshot value still shown
///   7. cascading Process → Group filter on /Admin/Users narrows the Group
///      dropdown when a Process is picked
///
/// No deep-link shortcuts; every navigation lands via a clicked link or the
/// canonical /Admin/* URL the sidebar exposes.
/// </summary>
public class US1_ProcessAdmin : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    private string[] _createdGroupNames = Array.Empty<string>();

    /// <summary>
    /// Spec 021 / FR-001 — US1 creates Groups under a Plantilla-bearing Process.
    /// The shared E2E fixture + <c>RegisterUserAsync→AssignAllGroups</c> would
    /// otherwise cross-wire every later-registered applicant into this Process,
    /// and <c>SubmitApplicationHandler.ResolveMinimumQuotationsAsync</c>
    /// (<c>FirstOrDefault</c> over the applicant's group memberships) would
    /// resolve their submit-time minimum quotations to this Process's
    /// ProcessPlantilla snapshot. Delete the groups so the shared fixture stays
    /// neutral for downstream submission tests.
    /// </summary>
    [TearDown]
    public async Task CleanUpCreatedGroupsAsync()
    {
        if (_createdGroupNames.Length == 0)
        {
            return;
        }
        var page = new AdminGroupsPage(Page);
        foreach (var name in _createdGroupNames)
        {
            try
            {
                await page.GoToIndexAsync(BaseUrl);
                if (await page.RowFor(name).CountAsync() == 0)
                {
                    continue;
                }
                await page.RowEditButton(name).ClickAsync();
                await page.DeleteGroupAsync();
                await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Groups(\\?.*)?$"));
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"US1 group cleanup: could not delete '{name}': {ex.Message}");
            }
        }
    }

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"procadmin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Process", "Admin", $"PADM-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [Test]
    public async Task FullFlow_CreateProcess_AssignPlantilla_Snapshot_IsIndependentOfBaseEdits()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);

        var processName = $"Crocus 2025 {unique}";
        var plantillaName = $"PlantillaMVP-v1-{unique}";

        var procPage = new ProcessAdminPage(Page);
        var planPage = new PlantillaAdminPage(Page);

        // ----- (1) /Admin/Processes index lands (no shortcut). -----
        await procPage.GoToIndexAsync(BaseUrl);
        await Expect(procPage.AreaWrapper).ToBeVisibleAsync();

        // ----- (2) Create the Process. -----
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(processName);
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Processes(\\?.*)?$"));
        await Expect(procPage.ProcessRow(processName)).ToBeVisibleAsync();

        // Read the Process id from the row's testid prefix so we can revisit
        // /Admin/Processes/{id} after the snapshot edit.
        var rowId = await procPage.ProcessRow(processName).GetAttributeAsync("data-testid");
        var match = Regex.Match(rowId ?? string.Empty, @"admin-process-row-(\d+)$");
        Assert.That(match.Success, Is.True, $"Could not parse process id from row testid: {rowId}");
        var processId = int.Parse(match.Groups[1].Value);

        // ----- (3) Create a base Plantilla with ≥ 1 ImpactTemplate. -----
        await planPage.GoToIndexAsync(BaseUrl);
        await planPage.GoToCreateAsync(BaseUrl);
        await planPage.CreatePlantillaAsync(plantillaName, minimumQuotationsPerItem: 3);
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Plantillas(\\?.*)?$"));
        await Expect(planPage.PlantillaRow(plantillaName)).ToBeVisibleAsync();

        // ----- (4) Assign the Plantilla on the Process detail. -----
        await procPage.GoToDetailsAsync(BaseUrl, processId);
        await Expect(procPage.AssignPlantillaForm).ToBeVisibleAsync();
        await procPage.AssignPlantillaAsync(plantillaName);
        // After the assign, the detail re-renders with the snapshot block visible.
        await Expect(procPage.PlantillaSnapshot).ToBeVisibleAsync();
        await Expect(procPage.PlantillaBaseName).ToHaveTextAsync(plantillaName);
        await Expect(procPage.PlantillaMinQuotations).ToHaveTextAsync("3");

        // ----- (5) Groups *Norte* / *Sur* / *Centro* under the Process. -----
        // Spec 021 / FR-001 — Groups are created from the Process detail page;
        // the owning Process is *this* Process by construction. The inline
        // "Nuevo grupo" form lives in the Groups panel of the same detail page.
        var groupNames = new[] { $"Norte-{unique}", $"Sur-{unique}", $"Centro-{unique}" };
        // Register for [TearDown] cleanup before creating — a mid-loop failure
        // still leaves the partially-created groups removable.
        _createdGroupNames = groupNames;
        foreach (var gname in groupNames)
        {
            await procPage.GoToDetailsAsync(BaseUrl, processId);
            await procPage.CreateGroupAsync(gname);
            await Expect(procPage.GroupRow(gname)).ToBeVisibleAsync();
        }

        // ----- (6) Snapshot-independence (SC-002). -----
        // Edit the base Plantilla's MinimumQuotationsPerItem; the Process
        // detail must still show the original snapshot value.
        var plantillaRow = planPage.PlantillaRow(plantillaName);
        await planPage.GoToIndexAsync(BaseUrl);
        var editLink = plantillaRow.Locator("[data-testid=\"admin-plantilla-edit\"]");
        await editLink.ClickAsync();
        await Expect(planPage.SnapshotBanner).ToBeVisibleAsync();
        await planPage.EditMinimumQuotationsAsync(7);
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Plantillas(\\?.*)?$"));

        await procPage.GoToDetailsAsync(BaseUrl, processId);
        await Expect(procPage.PlantillaMinQuotations).ToHaveTextAsync("3",
            new() { Timeout = 10_000 });

        // ----- (7) Cascading Process → Group filter on /Admin/Users. -----
        var usersPage = new AdminUsersPage(Page);
        await usersPage.GoToIndexAsync(BaseUrl);
        // Root carries display:contents (no box) — assert the level selects.
        await Expect(usersPage.FundFilter).ToBeVisibleAsync();
        await Expect(usersPage.ProcessFilter).ToBeVisibleAsync();
        await Expect(usersPage.GroupFilter).ToBeVisibleAsync();

        // FR-001 + FR-034 — picking *Crocus 2025* must narrow the Group dropdown
        // to exactly the three groups created under it in step (5). The three
        // groups genuinely belong to this Process (no "Migración inicial"
        // fallback), so the cascade is a real subset assertion now.
        await usersPage.SelectProcessByLabelAsync(processName);
        var scopedOptions = await usersPage.GroupFilter.Locator("option").AllTextContentsAsync();

        foreach (var gname in groupNames)
        {
            Assert.That(scopedOptions.Any(o => o.Contains(gname, StringComparison.Ordinal)), Is.True,
                $"FR-034 — Group dropdown scoped to '{processName}' must include '{gname}'. "
                + $"Got: {string.Join(" | ", scopedOptions)}");
        }
        // First option is the "Todos los grupos" placeholder; the rest are the
        // Process's three groups and nothing else.
        Assert.That(scopedOptions.Count, Is.EqualTo(groupNames.Length + 1),
            $"FR-001 / FR-034 — '{processName}' has exactly {groupNames.Length} groups (plus the "
            + $"placeholder). Got: {string.Join(" | ", scopedOptions)}");
    }

    /// <summary>
    /// Spec 021 / US1 — the ProcessPlantilla snapshot is immutable to base-Plantilla
    /// edits (FR-004), so changing a Process's values means detaching the snapshot
    /// and assigning a different base Plantilla. This drives that flow from the
    /// Process detail page: assign → detach → reassign a different base Plantilla,
    /// and asserts the snapshot carries the new base's values.
    /// </summary>
    [Test]
    public async Task DetachPlantilla_FromProcessDetail_AllowsAssigningADifferentBasePlantilla()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);

        var processName = $"Nexo 2026 {unique}";
        var firstPlantilla = $"PlantillaA-{unique}";
        var secondPlantilla = $"PlantillaB-{unique}";

        var procPage = new ProcessAdminPage(Page);
        var planPage = new PlantillaAdminPage(Page);

        // ----- Create the Process. -----
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(processName);
        await Expect(procPage.ProcessRow(processName)).ToBeVisibleAsync();
        var rowId = await procPage.ProcessRow(processName).GetAttributeAsync("data-testid");
        var processId = int.Parse(
            Regex.Match(rowId ?? string.Empty, @"admin-process-row-(\d+)$").Groups[1].Value);

        // ----- Two base Plantillas with distinct minimum quotations. -----
        await planPage.GoToCreateAsync(BaseUrl);
        await planPage.CreatePlantillaAsync(firstPlantilla, minimumQuotationsPerItem: 2);
        await planPage.GoToCreateAsync(BaseUrl);
        await planPage.CreatePlantillaAsync(secondPlantilla, minimumQuotationsPerItem: 5);

        // ----- Assign the first; snapshot reflects its value. -----
        await procPage.GoToDetailsAsync(BaseUrl, processId);
        await procPage.AssignPlantillaAsync(firstPlantilla);
        await Expect(procPage.PlantillaSnapshot).ToBeVisibleAsync();
        await Expect(procPage.PlantillaMinQuotations).ToHaveTextAsync("2");

        // ----- Detach — the control the Process detail was previously missing. -----
        await Expect(procPage.DetachPlantillaSubmit).ToBeVisibleAsync();
        await procPage.DetachPlantillaAsync();

        // The snapshot is gone and the assign form is offered again.
        await Expect(procPage.AssignPlantillaForm).ToBeVisibleAsync();

        // ----- Reassign a *different* base Plantilla; values follow the new base. -----
        await procPage.AssignPlantillaAsync(secondPlantilla);
        await Expect(procPage.PlantillaSnapshot).ToBeVisibleAsync();
        await Expect(procPage.PlantillaBaseName).ToHaveTextAsync(secondPlantilla);
        await Expect(procPage.PlantillaMinQuotations).ToHaveTextAsync("5");
    }

    /// <summary>
    /// Spec 021 / US1 / FR-003 — the "Campos requeridos" group on the Plantilla
    /// Edit form is a multi-checkbox bit-mask. Checking every flag and saving
    /// MUST persist every bit; the regression dropped all but the lowest bit
    /// because the checkbox group bound to a scalar instead of a collection.
    /// </summary>
    [Test]
    public async Task EditPlantilla_AllRequiredFieldFlags_PersistAcrossSave()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(unique);

        var plantillaName = $"PlantillaFlags-{unique}";
        var planPage = new PlantillaAdminPage(Page);
        long[] bits = { 1, 2, 4, 8 };

        // ----- Create a base Plantilla (Create leaves required flags unset). -----
        await planPage.GoToCreateAsync(BaseUrl);
        await planPage.CreatePlantillaAsync(plantillaName, minimumQuotationsPerItem: 3);
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Plantillas(\\?.*)?$"));
        await Expect(planPage.PlantillaRow(plantillaName)).ToBeVisibleAsync();

        // ----- Open Edit, check ALL four required-field flags, save. -----
        await planPage.PlantillaRow(plantillaName)
            .Locator("[data-testid=\"admin-plantilla-edit\"]").ClickAsync();
        foreach (var bit in bits)
        {
            await planPage.RequiredFieldCheckbox(bit).CheckAsync();
        }
        await planPage.EditSubmit.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Plantillas(\\?.*)?$"));

        // ----- Reopen Edit — every flag must still be checked. -----
        await planPage.PlantillaRow(plantillaName)
            .Locator("[data-testid=\"admin-plantilla-edit\"]").ClickAsync();
        foreach (var bit in bits)
        {
            await Expect(planPage.RequiredFieldCheckbox(bit)).ToBeCheckedAsync(
                new() { Timeout = 5_000 });
        }
    }
}
