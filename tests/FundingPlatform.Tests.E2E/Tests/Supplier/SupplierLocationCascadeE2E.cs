// Spec 025 — US1/US2/US3 cascade journeys, driven through the real UI (no
// deep-linking into routes the UI never exposes). Each test reaches the surface
// under test by clicking from the applicant/admin journey, drives the
// Provincia → Cantón → Distrito cascade, and asserts persistence in the seeded
// SQL DB (FK chain + composed display string).

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Supplier;

[TestFixture]
public class SupplierLocationCascadeE2E : AuthenticatedTestBase
{
    private const string Password = "Test123!";
    private string _quotationFile = string.Empty;

    [SetUp]
    public void SetUpQuotationFile()
    {
        _quotationFile = Path.Combine(Path.GetTempPath(), $"loc-quote-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_quotationFile, "Quotation placeholder content");
    }

    [TearDown]
    public void DeleteQuotationFile()
    {
        if (File.Exists(_quotationFile)) File.Delete(_quotationFile);
    }

    // ---- shared journey helpers ----

    /// <summary>Register + sign in an applicant, create a Draft, set Impact, add one item.
    /// Leaves the page on the draft editor. Returns the applicant email.</summary>
    private async Task<string> StartApplicantWithItemAsync(string tag)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"{tag}_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, Password, "Loc", "Tester", $"LOC-{uniqueId}");
        await LoginAsync(Page, email, Password);

        await Page.GotoAsync($"{BaseUrl}/Application");
        await Page.Locator("a:has-text('Iniciar acompañamiento')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));

        var appPage = new ApplicationPage(Page);
        await appPage.SelectCompanyIfPresentAsync();
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        // Spec 035 — per-item impact captured on the item form; add one impact-
        // complete item to reach the supplier-add form.
        var draft = new ApplicationDraftPage(Page);
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Horno industrial", 0, "Acero inoxidable, 60L", BaseUrl, withImpact: true);
        await Expect(draft.ItemRows).ToHaveCountAsync(1);
        return email;
    }

    private async Task ClickAddSupplierAsync()
    {
        await Page.Locator("a:has-text('Agregar proveedor')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Supplier/Add"));
    }

    private async Task<(int? ProvinceId, int? CantonId, int? DistrictId, string? Province)> ReadBranchLocationAsync(
        string legalId, bool defaultBranch)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT TOP 1 b.[ProvinceId], b.[CantonId], b.[DistrictId], b.[Province]
              FROM dbo.SupplierBranches b
              JOIN dbo.Suppliers s ON s.Id = b.SupplierId
             WHERE s.[LegalId] = @LegalId AND b.[IsDefault] = @IsDefault
             ORDER BY b.[Id] DESC;";
        cmd.Parameters.AddWithValue("@LegalId", legalId.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@IsDefault", defaultBranch ? 1 : 0);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.That(await r.ReadAsync(), Is.True, $"Branch for supplier {legalId} (default={defaultBranch}) must exist.");
        return (
            r["ProvinceId"] as int?,
            r["CantonId"] as int?,
            r["DistrictId"] as int?,
            r["Province"] as string);
    }

    private async Task<(int SupplierId, int BranchId)> ReadSupplierAndDefaultBranchAsync(string legalId)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.[Id] AS SupplierId, b.[Id] AS BranchId
              FROM dbo.Suppliers s
              JOIN dbo.SupplierBranches b ON b.SupplierId = s.Id AND b.[IsDefault] = 1
             WHERE s.[LegalId] = @LegalId;";
        cmd.Parameters.AddWithValue("@LegalId", legalId.ToUpperInvariant());
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.That(await r.ReadAsync(), Is.True, $"Supplier {legalId} + default branch must exist.");
        return ((int)r["SupplierId"], (int)r["BranchId"]);
    }

    /// <summary>Drives a cascade container (3 selects in order) at the given province
    /// index, waiting on the actual cascade-fetch responses so the selection can't be
    /// wiped by an in-flight fetch.</summary>
    private async Task DriveCascadeAsync(ILocator container, int provinceIndex)
    {
        var province = container.Locator("select").Nth(0);
        var canton = container.Locator("select").Nth(1);
        var district = container.Locator("select").Nth(2);

        var cantonsResp = Page.WaitForResponseAsync(r => r.Url.Contains("/api/cantons"));
        await province.SelectOptionAsync(new SelectOptionValue { Index = provinceIndex });
        await cantonsResp;
        await canton.Locator("option").Nth(1)
            .WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });

        var districtsResp = Page.WaitForResponseAsync(r => r.Url.Contains("/api/districts"));
        await canton.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await districtsResp;
        await district.Locator("option").Nth(1)
            .WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });

        await district.SelectOptionAsync(new SelectOptionValue { Index = 1 });
    }

    // ---- US1 (P1) — applicant new supplier ----

    [Test]
    public async Task US1_NewSupplier_CascadeNarrows_AndPersistsLocation()
    {
        var legalId = IdentificationData.CedulaJuridica($"US1-{Guid.NewGuid().ToString("N")[..8]}");
        await StartApplicantWithItemAsync("loc_us1");
        await ClickAddSupplierAsync();

        var supplier = new SupplierPage(Page);
        var outcome = await supplier.SearchByLegalIdAsync(legalId);
        Assert.That(outcome, Is.EqualTo("Empty"), "An unmatched cédula must show the Nuevo proveedor panel.");

        var province = supplier.NewSupplierLocation.Locator("select").Nth(0);
        var canton = supplier.NewSupplierLocation.Locator("select").Nth(1);
        var district = supplier.NewSupplierLocation.Locator("select").Nth(2);

        // Cantón + Distrito start empty (only the placeholder option).
        await Expect(canton.Locator("option")).ToHaveCountAsync(1);
        await Expect(district.Locator("option")).ToHaveCountAsync(1);

        // Provincia → Cantón narrows.
        var cantonsResp = Page.WaitForResponseAsync(r => r.Url.Contains("/api/cantons"));
        await province.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await cantonsResp;
        await canton.Locator("option").Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Attached });
        Assert.That(await canton.Locator("option").CountAsync(), Is.GreaterThan(1), "Cantón must narrow to the province.");

        // Cantón → Distrito narrows.
        var districtsResp = Page.WaitForResponseAsync(r => r.Url.Contains("/api/districts"));
        await canton.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await districtsResp;
        await district.Locator("option").Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Attached });
        Assert.That(await district.Locator("option").CountAsync(), Is.GreaterThan(1), "Distrito must narrow to the cantón.");

        // Changing Provincia resets the lower tiers (Distrito back to placeholder-only).
        await province.SelectOptionAsync(new SelectOptionValue { Index = 2 });
        await Expect(district.Locator("option")).ToHaveCountAsync(1);

        // Complete the chain + the form, submit.
        await supplier.NewSupplierNameInput.FillAsync($"Proveedor {legalId}");
        await supplier.NewSupplierBranchNameInput.FillAsync("Sede principal");
        await supplier.SelectFirstLocationAsync(supplier.NewSupplierLocation);
        await supplier.FillQuotationFieldsAsync(1234m, "2027-12-31", _quotationFile);
        await supplier.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Persistence: the principal branch carries the three FKs + a composed display.
        var loc = await ReadBranchLocationAsync(legalId, defaultBranch: true);
        Assert.Multiple(() =>
        {
            Assert.That(loc.ProvinceId, Is.Not.Null, "ProvinceId persisted.");
            Assert.That(loc.CantonId, Is.Not.Null, "CantonId persisted.");
            Assert.That(loc.DistrictId, Is.Not.Null, "DistrictId persisted.");
            Assert.That(loc.Province, Does.Contain(","), "Composed 'Distrito, Cantón, Provincia' display persisted.");
        });
    }

    [Test]
    public async Task US1_NewSupplier_IncompleteLocation_RejectedServerSide()
    {
        var legalId = IdentificationData.CedulaJuridica($"US1B-{Guid.NewGuid().ToString("N")[..8]}");
        await StartApplicantWithItemAsync("loc_us1b");
        await ClickAddSupplierAsync();

        var supplier = new SupplierPage(Page);
        Assert.That(await supplier.SearchByLegalIdAsync(legalId), Is.EqualTo("Empty"));
        await supplier.NewSupplierNameInput.FillAsync($"Proveedor {legalId}");
        await supplier.NewSupplierBranchNameInput.FillAsync("Sede principal");
        await supplier.FillQuotationFieldsAsync(999m, "2027-12-31", _quotationFile);
        // Defeat the client `required` gate so the POST reaches the server with empty
        // location → aggregated server-side validation (FR-005) blocks the write.
        await supplier.NewSupplierLocation.Locator("select")
            .EvaluateAllAsync<bool>("els => { els.forEach(e => e.removeAttribute('required')); return true; }");
        await supplier.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Supplier/Add"));
        await Expect(supplier.ValidationSummary).ToContainTextAsync("provincia");

        // No supplier row was created for the rejected attempt.
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.Suppliers WHERE [LegalId] = @LegalId;";
        cmd.Parameters.AddWithValue("@LegalId", legalId.ToUpperInvariant());
        Assert.That(Convert.ToInt32(await cmd.ExecuteScalarAsync()), Is.EqualTo(0),
            "An incomplete-location submit must not create a supplier.");
    }

    [Test]
    public async Task US1_NewSupplier_MissingDistrict_ShowsVisibleError_NoSilentBlock()
    {
        // Regression for the reported bug: the location selects were `required` AND
        // data-searchable (spec 031), which clips the native <select> to 1px — so the
        // browser's native required-validation bubble fired on an invisible element and the
        // submit was silently blocked with NO visible error. The `required` attribute was
        // removed so the form reaches the server, whose es-CR validation surfaces a VISIBLE
        // message in the validation summary.
        var legalId = IdentificationData.CedulaJuridica($"US1C-{Guid.NewGuid().ToString("N")[..8]}");
        await StartApplicantWithItemAsync("loc_us1c");
        await ClickAddSupplierAsync();

        var supplier = new SupplierPage(Page);
        Assert.That(await supplier.SearchByLegalIdAsync(legalId), Is.EqualTo("Empty"));
        await supplier.NewSupplierNameInput.FillAsync($"Proveedor {legalId}");
        await supplier.NewSupplierBranchNameInput.FillAsync("Sede principal");
        await supplier.FillQuotationFieldsAsync(999m, "2027-12-31", _quotationFile);

        // Select Provincia + Cantón but leave Distrito empty (the reported scenario).
        var province = supplier.NewSupplierLocation.Locator("select").Nth(0);
        var canton = supplier.NewSupplierLocation.Locator("select").Nth(1);
        var cantonsResp = Page.WaitForResponseAsync(r => r.Url.Contains("/api/cantons"));
        await province.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await cantonsResp;
        await canton.Locator("option").Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Attached });
        var districtsResp = Page.WaitForResponseAsync(r => r.Url.Contains("/api/districts"));
        await canton.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await districtsResp;
        await supplier.SubmitButton.ClickAsync();

        // The submit is no longer silently blocked: it reaches the server, which re-renders
        // /Supplier/Add with a VISIBLE es-CR district error in the validation summary.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Supplier/Add"));
        await Expect(supplier.ValidationSummary).ToContainTextAsync("distrito");
    }

    [Test]
    public async Task SeededSampleSuppliers_ExistWithExpectedRegulatoryProfiles()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.[LegalId], s.[HaciendaStatus], s.[CcssStatus], s.[SicopStatus], s.[IsPmeOrPyme], s.[VerificationStatus],
                   (SELECT COUNT(*) FROM dbo.SupplierBranches b WHERE b.SupplierId = s.[Id] AND b.[IsDefault] = 1) AS DefaultBranches
              FROM dbo.Suppliers s
             WHERE s.[LegalId] IN (N'1-111-111111', N'2-222-222222', N'3-333-333333', N'5-555-555555', N'6-666-666666');";
        var rows = new Dictionary<string, (int H, int C, int S, bool P, int V, int B)>();
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                rows[(string)r["LegalId"]] = (
                    Convert.ToInt32(r["HaciendaStatus"]), Convert.ToInt32(r["CcssStatus"]), Convert.ToInt32(r["SicopStatus"]),
                    Convert.ToBoolean(r["IsPmeOrPyme"]), Convert.ToInt32(r["VerificationStatus"]), Convert.ToInt32(r["DefaultBranches"]));
            }
        }

        Assert.That(rows, Has.Count.EqualTo(5), "All 5 sample suppliers must be seeded.");
        // 1/2/3 — all regulatories al día (Hacienda 2, CCSS 2, SICOP 2), not pyme.
        foreach (var legal in new[] { "1-111-111111", "2-222-222222", "3-333-333333" })
        {
            Assert.That(rows[legal].H, Is.EqualTo(2), $"{legal} Hacienda al día");
            Assert.That(rows[legal].C, Is.EqualTo(2), $"{legal} CCSS al día");
            Assert.That(rows[legal].S, Is.EqualTo(2), $"{legal} SICOP sin sanciones");
            Assert.That(rows[legal].P, Is.False, $"{legal} not pyme");
        }
        Assert.That(rows["5-555-555555"].P, Is.True, "5-555 is pyme");
        Assert.That(rows["5-555-555555"].C, Is.EqualTo(2), "5-555 CCSS al día");
        Assert.That(rows["6-666-666666"].C, Is.EqualTo(1), "6-666 CCSS sin inscripción (1)");
        Assert.That(rows["6-666-666666"].H, Is.EqualTo(2), "6-666 Hacienda al día");
        foreach (var v in rows.Values)
        {
            Assert.That(v.V, Is.EqualTo(2), "Seeded supplier is Verified");
            Assert.That(v.B, Is.EqualTo(1), "Seeded supplier has exactly one default branch");
        }
    }

    // ---- US2 (P2) — applicant new branch on existing supplier ----

    [Test]
    public async Task US2_NewBranchOnExistingSupplier_CascadePersists()
    {
        var legalId = IdentificationData.CedulaJuridica($"US2-{Guid.NewGuid().ToString("N")[..8]}");
        await StartApplicantWithItemAsync("loc_us2");

        // First, create the supplier (US1 path) on item #1 so a later lookup is a Hit.
        await ClickAddSupplierAsync();
        var s1 = new SupplierPage(Page);
        Assert.That(await s1.SearchByLegalIdAsync(legalId), Is.EqualTo("Empty"));
        await s1.FillNewSupplierFormAsync($"Proveedor {legalId}", "Sede principal");
        await s1.FillQuotationFieldsAsync(1000m, "2027-12-31", _quotationFile);
        await s1.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Add a SECOND item — a supplier may carry only one quotation per item, so the
        // existing supplier (with its new branch) is quoted on a fresh item.
        var draft = new ApplicationDraftPage(Page);
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);
        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, "Mesa de trabajo", 0, "Acero inoxidable, 2m", BaseUrl, withImpact: true);
        await Expect(draft.ItemRows).ToHaveCountAsync(2);

        // Add a new branch under the same supplier via the new-branch cascade (item #2).
        await Page.Locator("a:has-text('Agregar proveedor')").Last.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Supplier/Add"));
        var s2 = new SupplierPage(Page);
        Assert.That(await s2.SearchByLegalIdAsync(legalId), Is.EqualTo("Hit"),
            "The applicant's own Draft supplier must resolve as a Hit.");
        await s2.OpenAddNewBranchPanelAsync();
        await s2.FillNewBranchFormAsync("Sucursal Cartago");
        await s2.FillQuotationFieldsAsync(1500m, "2027-12-31", _quotationFile);
        // A single-branch supplier pre-checks its default-branch radio (collapsed
        // picker); clear it so the POST takes the new-branch path (spec-013 dispatch
        // prefers SelectedBranchId when set) rather than reusing the existing branch.
        await Page.Locator("input[name=SelectedBranchId]")
            .EvaluateAllAsync<bool>("els => { els.forEach(e => e.checked = false); return true; }");
        await s2.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // The supplier now has two branches; the new (non-default) one carries the chain.
        await using (var conn = new SqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM dbo.SupplierBranches b
                  JOIN dbo.Suppliers s ON s.Id = b.SupplierId
                 WHERE s.[LegalId] = @LegalId;";
            cmd.Parameters.AddWithValue("@LegalId", legalId.ToUpperInvariant());
            Assert.That(Convert.ToInt32(await cmd.ExecuteScalarAsync()), Is.EqualTo(2), "Supplier must have 2 branches.");
        }

        var branch = await ReadBranchLocationAsync(legalId, defaultBranch: false);
        Assert.Multiple(() =>
        {
            Assert.That(branch.ProvinceId, Is.Not.Null);
            Assert.That(branch.CantonId, Is.Not.Null);
            Assert.That(branch.DistrictId, Is.Not.Null);
            Assert.That(branch.Province, Does.Contain(","));
        });
    }

    // ---- US3 (P3) — admin branch edit ----

    [Test]
    public async Task US3_AdminBranchEdit_PreselectsAndChangesLocation()
    {
        // Applicant creates a supplier (default branch carries a location from US1 path).
        var legalId = IdentificationData.CedulaJuridica($"US3-{Guid.NewGuid().ToString("N")[..8]}");
        await StartApplicantWithItemAsync("loc_us3");
        await ClickAddSupplierAsync();
        var sp = new SupplierPage(Page);
        Assert.That(await sp.SearchByLegalIdAsync(legalId), Is.EqualTo("Empty"));
        await sp.FillNewSupplierFormAsync($"Proveedor {legalId}", "Sede principal");
        await sp.FillQuotationFieldsAsync(1000m, "2027-12-31", _quotationFile);
        await sp.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var (supplierId, branchId) = await ReadSupplierAndDefaultBranchAsync(legalId);
        var before = await ReadBranchLocationAsync(legalId, defaultBranch: true);

        // Switch to an admin session.
        await Context.ClearCookiesAsync();
        var adminEmail = $"loc_us3_admin_{Guid.NewGuid().ToString("N")[..6]}@example.com";
        await RegisterUserAsync(Page, adminEmail, Password, "Loc", "Admin", $"LADM-{Guid.NewGuid().ToString("N")[..6]}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, Password);

        var detail = new PageObjects.Admin.AdminSupplierDetailPage(Page);
        await detail.GoToAsync(BaseUrl, supplierId);

        var locationContainer = Page.GetByTestId($"admin-branch-location-{branchId}");
        var province = locationContainer.Locator("select").Nth(0);

        // Open the branch-edit form and confirm the cascade is pre-selected to current values.
        await detail.BranchEditToggle(branchId).ClickAsync();
        await Expect(province).ToBeVisibleAsync();
        Assert.That(await province.InputValueAsync(), Is.Not.Empty, "Provincia must be pre-selected to the branch's value.");

        // Change to a different province → cantón → distrito and save.
        await DriveCascadeAsync(locationContainer, provinceIndex: 3);
        await detail.BranchEditSave(branchId).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Admin/Suppliers/\d+"));

        var after = await ReadBranchLocationAsync(legalId, defaultBranch: true);
        Assert.Multiple(() =>
        {
            Assert.That(after.DistrictId, Is.Not.Null, "DistrictId must remain set after edit.");
            Assert.That(after.DistrictId, Is.Not.EqualTo(before.DistrictId), "The saved distrito must reflect the change.");
            Assert.That(after.Province, Does.Contain(","), "Composed display string must be recomposed on save.");
        });
    }
}
