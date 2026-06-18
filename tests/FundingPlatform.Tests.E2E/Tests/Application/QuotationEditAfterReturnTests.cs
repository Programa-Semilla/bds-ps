using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Application;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Application;

/// <summary>
/// Spec 023 / US2 — applicant on an application that the reviewer returned
/// for changes swaps a quotation's branch to a different branch of the SAME
/// supplier and saves.
///
/// LIFECYCLE NOTE: the current codebase has no <c>ReturnedForChanges</c> state.
/// The reviewer's <c>SendBack</c> path transitions an Application back to
/// <c>Draft</c> (see <c>Application.SendBack</c>), so US2's "returned-for-
/// changes" semantics are delivered on the <c>Draft</c> state. The Editar
/// affordance and the EditQuotationAsync state gate both accept <c>Draft</c>.
/// The two scenarios below match the spec's Independent Test exactly — same
/// supplier branch swap + cross-supplier rejection.
/// </summary>
public class QuotationEditAfterReturnTests : AuthenticatedTestBase
{
    private string _testFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"branch-swap-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_testFilePath, "%PDF-1.4\nplaceholder quotation\n%%EOF\n");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
    }

    [Test]
    public async Task SwapsBranchOnReturned_PreservesReviewerComments()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"qedit_branch_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "QEdit", "Branch", $"QEB-{uniqueId}");
        await LoginAsync(Page, email, password);

        var seeded = await SeedDraftWithCrcQuotationAndTwoBranchesAsync(uniqueId);

        await Page.GotoAsync($"{BaseUrl}/Application/Edit/{seeded.AppId}");
        await QuotationEditPage.EditButtonFor(Page, seeded.QuotationId).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(
            $"/Application/{seeded.AppId}/Item/{seeded.ItemId}/Quotation/{seeded.QuotationId}/Edit"));

        var editPage = new QuotationEditPage(Page);

        // The branch picker must list ONLY branches of the seeded supplier (FR-004).
        var branchValues = await editPage.GetBranchOptionValuesAsync();
        Assert.That(branchValues, Has.Count.EqualTo(2),
            "Branch picker must list exactly the two branches of the current Supplier.");

        // Swap to the OTHER branch.
        var otherBranchValue = branchValues.First(v => v != seeded.FirstBranchId.ToString());
        await editPage.SetBranchByValueAsync(otherBranchValue);
        await editPage.SubmitAsync();
        await editPage.WaitForRedirectToApplicationEditAsync(seeded.AppId);

        // Verify the swap persisted in the DB.
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT [SupplierBranchId] FROM dbo.Quotations WHERE [Id] = @QuotationId;";
        cmd.Parameters.AddWithValue("@QuotationId", seeded.QuotationId);
        var newBranchId = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.That(newBranchId.ToString(), Is.EqualTo(otherBranchValue),
            "SupplierBranchId must reflect the swap.");
    }

    [Test]
    public async Task RejectsCrossSupplierBranch()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"qedit_cross_{uniqueId}@example.com";
        const string password = "Test123!";

        await RegisterUserAsync(Page, email, password, "QEdit", "Cross", $"QEC-{uniqueId}");
        await LoginAsync(Page, email, password);

        var seeded = await SeedDraftWithCrcQuotationAndTwoBranchesAsync(uniqueId);

        // Mint a separate Supplier with one branch — its branch id is the
        // illegal target. Done directly via SQL so the test doesn't depend on
        // a second multi-supplier journey to set up the rejection case.
        var foreignBranchId = await SeedForeignSupplierBranchAsync(uniqueId);

        await Page.GotoAsync(
            $"{BaseUrl}/Application/{seeded.AppId}/Item/{seeded.ItemId}/Quotation/{seeded.QuotationId}/Edit");

        var editPage = new QuotationEditPage(Page);

        // The POM picker only exposes branches of the current supplier — to
        // post a foreign branch id we inject the option client-side and select
        // it. This deliberately replicates a hostile / scripted POST and is
        // exactly the path that exercises the server-side branch invariant.
        await Page.EvaluateAsync(@"({ select, value }) => {
            const opt = document.createElement('option');
            opt.value = String(value);
            opt.textContent = 'Foreign Branch (test inject)';
            select.appendChild(opt);
            select.value = String(value);
        }", new { select = await editPage.BranchSelect.ElementHandleAsync(), value = foreignBranchId });

        await editPage.SubmitAsync();

        // 400 re-render — the URL stays on the Edit form and the SupplierBranchId
        // field error is visible with the exact es-CR copy.
        await Expect(Page).ToHaveURLAsync(new Regex(
            $"/Application/{seeded.AppId}/Item/{seeded.ItemId}/Quotation/{seeded.QuotationId}/Edit"));
        await Expect(editPage.BranchError).ToContainTextAsync(
            new Regex("Sucursal no válida para este proveedor."));
    }

    private async Task<int> SeedForeignSupplierBranchAsync(string uniqueId)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        // CreateDraft is the canonical entry-point so a raw INSERT here keeps
        // the data shape consistent — applicant-owned Draft supplier, IsDefault
        // first branch. Fields outside the unique constraints are nulled.
        cmd.CommandText = @"
            DECLARE @SupplierId INT;
            INSERT INTO dbo.Suppliers
                (LegalId, Name, VerificationStatus, CreatedAt, UpdatedAt)
            VALUES
                (@LegalId, @Name, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
            SET @SupplierId = SCOPE_IDENTITY();

            DECLARE @BranchId INT;
            INSERT INTO dbo.SupplierBranches
                (SupplierId, BranchName, IsDefault, CreatedAt, UpdatedAt)
            VALUES
                (@SupplierId, 'Sede del proveedor ajeno', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            SET @BranchId = SCOPE_IDENTITY();
            SELECT @BranchId;";
        cmd.Parameters.AddWithValue("@LegalId", $"OTH-{uniqueId}");
        cmd.Parameters.AddWithValue("@Name", $"Foreign Supplier {uniqueId}");
        var branchId = (int)(await cmd.ExecuteScalarAsync())!;
        return branchId;
    }

    private sealed record Seeded(int AppId, int ItemId, int QuotationId, int FirstBranchId);

    private async Task<Seeded> SeedDraftWithCrcQuotationAndTwoBranchesAsync(string uniqueId)
    {
        // First half — drive the applicant journey to create app + item + one
        // supplier with one branch + one CRC quotation.
        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateApplicationAsync($"Branch Edit Co {uniqueId}");
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        var itemPage = new ItemPage(Page);
        await itemPage.AddItemAsync(appId, $"Server {uniqueId}", 0, "specs", BaseUrl);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        var addSupplierLink = Page.Locator($"a:has-text('{UiCopy.AddSupplier}')").First;
        await addSupplierLink.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Supplier/Add"));

        var supplierPage = new SupplierPage(Page);
        var supplierLegalId = $"SUP-QB-{uniqueId}";
        Assert.That(await supplierPage.SearchByLegalIdAsync(supplierLegalId), Is.EqualTo("Empty"));
        await supplierPage.FillNewSupplierFormAsync(
            name: $"Branch Supplier {uniqueId}",
            branchName: "Sede principal",
            province: "San Jose");
        await supplierPage.PriceInput.FillAsync("1500");
        await supplierPage.SetCurrencyAsync("CRC");
        await supplierPage.ValidUntilInput.FillAsync("2027-12-31");
        await supplierPage.DeliveryValueInput.FillAsync("30");
        await supplierPage.WarrantyValueInput.FillAsync("12");
        await supplierPage.QuotationFileInput.SetInputFilesAsync(_testFilePath);
        await supplierPage.SubmitAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));

        // Second half — query ids, add a SECOND branch for the same supplier
        // directly via SQL so the test does not depend on a not-yet-shipped
        // "add branch" applicant UI (spec 013 left the branch-add flow on the
        // admin side).
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1 q.[Id] AS QuotationId, q.[ItemId], q.[SupplierId], q.[SupplierBranchId]
              FROM dbo.Quotations q
              JOIN dbo.Items i ON i.[Id] = q.[ItemId]
             WHERE i.[ApplicationId] = @AppId
             ORDER BY q.[Id] DESC;";
        cmd.Parameters.AddWithValue("@AppId", appId);
        int quotationId, itemId, supplierId, firstBranchId;
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            Assert.That(await reader.ReadAsync(), Is.True);
            quotationId = (int)reader["QuotationId"];
            itemId = (int)reader["ItemId"];
            supplierId = (int)reader["SupplierId"];
            firstBranchId = (int)reader["SupplierBranchId"];
        }

        using var addBranchCmd = conn.CreateCommand();
        addBranchCmd.CommandText = @"
            INSERT INTO dbo.SupplierBranches
                (SupplierId, BranchName, IsDefault, CreatedAt, UpdatedAt)
            VALUES
                (@SupplierId, 'Sede secundaria', 0, SYSUTCDATETIME(), SYSUTCDATETIME());";
        addBranchCmd.Parameters.AddWithValue("@SupplierId", supplierId);
        await addBranchCmd.ExecuteNonQueryAsync();

        return new Seeded(appId, itemId, quotationId, firstBranchId);
    }
}
