using System.Text;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 034 — admin CSV bulk provisioning of Solicitante accounts. The ephemeral
/// seed provides Fund "Fondo General" → Process "Migración inicial" → Groups
/// Norte/Sur/Centro, which form valid chains here. Batch recipients use the
/// "@programa-semilla.test" domain so the non-prod allowlist lets the set-password
/// invitation through to smtp4dev (mirrors the seed-user convention).
/// </summary>
public class BatchUserCreateTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    // Canonical es-CR template header (spec 037 — trailing company column).
    private const string Header =
        "Grupo,Proceso,Fondo,Nombre,Apellido 1,Apellido 2,Email,Teléfono,Cédula,Código de usuario,Nombre de la empresa";

    private async Task SignInAsAdminAsync(string unique)
    {
        var adminEmail = $"batch_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Batch", "Admin", $"BADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    /// <summary>Writes the given CSV body (header prepended) to a temp .csv file.</summary>
    private static string WriteCsv(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"batch-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, Header + "\n" + body, new UTF8Encoding(false));
        return path;
    }

    [Test]
    public async Task AllValid_CreatesUsers_AndSendsInvitations()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SignInAsAdminAsync(unique);

        var email1 = $"batch_{unique}_1@programa-semilla.test";
        var email2 = $"batch_{unique}_2@programa-semilla.test";
        var csv = WriteCsv(string.Join("\n", new[]
        {
            $"Norte,Migración inicial,Fondo General,Ana,Rojas,Mora,{email1},506 8888 1111,{IdentificationData.CedulaFisica($"BU1-{unique}")},BUC-{unique}-1,Empresa Demo",
            $"Sur,Migración inicial,Fondo General,Luis,Mora,,{email2},7777-2222,{IdentificationData.CedulaFisica($"BU2-{unique}")},BUC-{unique}-2,Empresa Demo",
        }));

        var page = new AdminBatchUsersPage(Page);
        await page.GoToAsync(BaseUrl);
        await page.UploadAsync(csv);

        await Expect(page.Summary).ToBeVisibleAsync();
        Assert.That(await page.SucceededCountValueAsync(), Is.EqualTo(2));
        Assert.That(await page.ErroredCountValueAsync(), Is.EqualTo(0));

        // Both accounts appear in the users list.
        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(email1);
        await Expect(list.RowFor(email1)).ToBeVisibleAsync();
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(email2);
        await Expect(list.RowFor(email2)).ToBeVisibleAsync();

        // Two set-password invitations captured (allowlist passes the test domain).
        if (MailCapture is null)
        {
            Assert.Inconclusive("smtp4dev sidecar unavailable (NFR-007 degraded mode).");
            return;
        }
        var captured = await MailCapture.WaitForAsync(
            2, TimeSpan.FromSeconds(15),
            m => m.ToAddresses.Any(a => a.Contains($"batch_{unique}_")));
        Assert.That(captured.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task Mixed_ReportsSucceededAndErrored()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SignInAsAdminAsync(unique);

        var goodEmail = $"batch_{unique}_ok@programa-semilla.test";
        var badEmail = $"batch_{unique}_bad@programa-semilla.test";
        var csv = WriteCsv(string.Join("\n", new[]
        {
            // valid
            $"Norte,Migración inicial,Fondo General,Ana,Rojas,,{goodEmail},,{IdentificationData.CedulaFisica($"BM1-{unique}")},BMC-{unique}-1,Empresa Demo",
            // blank email
            $"Norte,Migración inicial,Fondo General,Bob,Soto,,,,{IdentificationData.CedulaFisica($"BM2-{unique}")},BMC-{unique}-2,Empresa Demo",
            // unrecognized id shape (10 digits → no inferred individual type → errored)
            $"Norte,Migración inicial,Fondo General,Cyn,Vega,,{badEmail},,1234567890,BMC-{unique}-3,Empresa Demo",
        }));

        var page = new AdminBatchUsersPage(Page);
        await page.GoToAsync(BaseUrl);
        await page.UploadAsync(csv);

        await Expect(page.Summary).ToBeVisibleAsync();
        Assert.That(await page.SucceededCountValueAsync(), Is.EqualTo(1));
        Assert.That(await page.ErroredCountValueAsync(), Is.EqualTo(2));

        // The errored section shows visible es-CR reasons.
        await Expect(page.ErroredRows.First).ToBeVisibleAsync();
        var erroredText = await page.ErroredRows.AllInnerTextsAsync();
        var joined = string.Join(" | ", erroredText);
        Assert.That(joined, Does.Contain("correo"));          // blank-email reason
        Assert.That(joined, Does.Contain("identificación")); // unrecognized-id reason

        // The valid row created a user; the bad rows did not.
        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(goodEmail);
        await Expect(list.RowFor(goodEmail)).ToBeVisibleAsync();
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(badEmail);
        await Expect(list.RowFor(badEmail)).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task ChainMismatch_RowSkipped()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SignInAsAdminAsync(unique);

        var goodEmail = $"batch_{unique}_chainok@programa-semilla.test";
        var badEmail = $"batch_{unique}_chainbad@programa-semilla.test";
        var csv = WriteCsv(string.Join("\n", new[]
        {
            // coherent chain
            $"Norte,Migración inicial,Fondo General,Ana,Rojas,,{goodEmail},,{IdentificationData.CedulaFisica($"BC1-{unique}")},BCC-{unique}-1,Empresa Demo",
            // wrong chain — real group, but a Fondo that does not exist
            $"Norte,Migración inicial,Fondo Inexistente,Bob,Soto,,{badEmail},,{IdentificationData.CedulaFisica($"BC2-{unique}")},BCC-{unique}-2,Empresa Demo",
        }));

        var page = new AdminBatchUsersPage(Page);
        await page.GoToAsync(BaseUrl);
        await page.UploadAsync(csv);

        await Expect(page.Summary).ToBeVisibleAsync();
        Assert.That(await page.SucceededCountValueAsync(), Is.EqualTo(1));
        Assert.That(await page.ErroredCountValueAsync(), Is.EqualTo(1));

        var erroredText = string.Join(" | ", await page.ErroredRows.AllInnerTextsAsync());
        Assert.That(erroredText, Does.Contain("fondo")); // chain reason: fondo no existe

        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(goodEmail);
        await Expect(list.RowFor(goodEmail)).ToBeVisibleAsync();
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(badEmail);
        await Expect(list.RowFor(badEmail)).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task HeaderMismatch_RejectsWholeFile()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SignInAsAdminAsync(unique);

        var path = Path.Combine(Path.GetTempPath(), $"batch-badheader-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "col1,col2\nfoo,bar\n", new UTF8Encoding(false));

        var page = new AdminBatchUsersPage(Page);
        await page.GoToAsync(BaseUrl);
        await page.UploadAsync(path);

        // FR-003 — one es-CR file-level message, no result page, nothing created.
        await Expect(page.FileError).ToBeVisibleAsync();
        await Expect(page.FileError).ToContainTextAsync("columnas");
        await Expect(page.Summary).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task TooManyRows_RejectsWholeFile()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SignInAsAdminAsync(unique);

        var lines = Enumerable.Range(1, 201).Select(i =>
            $"Norte,Migración inicial,Fondo General,N{i},A{i},,row{i}_{unique}@programa-semilla.test,,{IdentificationData.CedulaFisica($"BT{i}-{unique}")},BTC-{unique}-{i},Empresa Demo");
        var csv = WriteCsv(string.Join("\n", lines));

        var page = new AdminBatchUsersPage(Page);
        await page.GoToAsync(BaseUrl);
        await page.UploadAsync(csv);

        await Expect(page.FileError).ToBeVisibleAsync();
        await Expect(page.FileError).ToContainTextAsync("200");
        await Expect(page.Summary).Not.ToBeVisibleAsync();
    }

    // ---- Spec 037 — company column -------------------------------------------

    [Test]
    public async Task Template_IncludesCompanyColumn()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SignInAsAdminAsync(unique);

        // Use the browser context's request API so the admin auth cookie + the
        // dev TLS cert handling carry over automatically.
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/Admin/Users/Batch/Template");
        Assert.That(response.Ok, Is.True);
        var csvText = await response.TextAsync();
        Assert.That(csvText, Does.Contain("Nombre de la empresa"));
    }

    [Test]
    public async Task BlankCompany_RowErrored()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SignInAsAdminAsync(unique);

        var goodEmail = $"batch_{unique}_co_ok@programa-semilla.test";
        var badEmail = $"batch_{unique}_co_blank@programa-semilla.test";
        var csv = WriteCsv(string.Join("\n", new[]
        {
            // valid (with company)
            $"Norte,Migración inicial,Fondo General,Ana,Rojas,,{goodEmail},,{IdentificationData.CedulaFisica($"CO1-{unique}")},COC-{unique}-1,Empresa Válida",
            // blank company cell → errored
            $"Norte,Migración inicial,Fondo General,Bob,Soto,,{badEmail},,{IdentificationData.CedulaFisica($"CO2-{unique}")},COC-{unique}-2,",
        }));

        var page = new AdminBatchUsersPage(Page);
        await page.GoToAsync(BaseUrl);
        await page.UploadAsync(csv);

        await Expect(page.Summary).ToBeVisibleAsync();
        Assert.That(await page.SucceededCountValueAsync(), Is.EqualTo(1));
        Assert.That(await page.ErroredCountValueAsync(), Is.EqualTo(1));

        var erroredText = string.Join(" | ", await page.ErroredRows.AllInnerTextsAsync());
        Assert.That(erroredText, Does.Contain("empresa"));

        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(badEmail);
        await Expect(list.RowFor(badEmail)).Not.ToBeVisibleAsync();
    }
}
