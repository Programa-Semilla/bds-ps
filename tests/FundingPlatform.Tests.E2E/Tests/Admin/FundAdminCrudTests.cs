using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 029 / US1 (T037) — admin Fund (Fondo) catalog E2E: create (no PDF),
/// regulation upload/remove, blank-name + non-PDF rejection, archive/reactivate
/// lifecycle. Drives the real /Admin/Funds UI; confirmations go through the
/// spec-024 shared confirm modal.
/// </summary>
public class FundAdminCrudTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private string _pdfPath = string.Empty;
    private string _notPdfPath = string.Empty;

    [SetUp]
    public void SetUpFiles()
    {
        _pdfPath = Path.Combine(Path.GetTempPath(), $"reg-{Guid.NewGuid():N}.pdf");
        // Real PDF magic bytes so the controller's %PDF- check passes.
        File.WriteAllBytes(_pdfPath, "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());
        _notPdfPath = Path.Combine(Path.GetTempPath(), $"reg-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_notPdfPath, "this is not a pdf");
    }

    [TearDown]
    public void CleanUpFiles()
    {
        if (File.Exists(_pdfPath)) File.Delete(_pdfPath);
        if (File.Exists(_notPdfPath)) File.Delete(_notPdfPath);
    }

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"fundadmin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Fund", "Admin", $"FADM-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    private ILocator Row(string name) =>
        Page.Locator("tr[data-testid^=admin-fund-row-]").Filter(new() { HasText = name });

    private async Task ConfirmAsync()
        => await Page.Locator("#fl-shared-confirm-modal [data-testid=confirm-button]").ClickAsync();

    private async Task CreateFundAsync(string name, string description)
    {
        await Page.GotoAsync($"{BaseUrl}/Admin/Funds/Create");
        await Page.Locator("[data-testid=admin-fund-name-input]").FillAsync(name);
        await Page.Locator("[data-testid=admin-fund-description-input]").FillAsync(description);
        await Page.Locator("[data-testid=admin-fund-create-submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Funds(\\?.*)?$"));
    }

    private async Task OpenDetailsAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/Admin/Funds");
        await Row(name).Locator("[data-testid=admin-fund-details]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Admin/Funds/\d+$"));
    }

    [Test]
    public async Task Admin_CreatesFund_AppearsActive_ThenArchivesAndReactivates()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);
        var name = $"Fondo-{u}";

        await CreateFundAsync(name, "Descripción del fondo de prueba.");
        await Expect(Row(name)).ToBeVisibleAsync();
        await Expect(Row(name).Locator("[data-testid=admin-fund-status]")).ToHaveTextAsync(new Regex("Activo"));

        await OpenDetailsAsync(name);
        await Expect(Page.Locator("[data-testid=admin-fund-status]").First).ToHaveTextAsync(new Regex("Activo"));

        // Archive (spec-024 confirm) → status Archivado.
        await Page.Locator("[data-testid=admin-fund-archive]").ClickAsync();
        await ConfirmAsync();
        await Expect(Page.Locator("[data-testid=admin-fund-status]").First).ToHaveTextAsync(new Regex("Archivado"));

        // Reactivate → status Activo.
        await Page.Locator("[data-testid=admin-fund-reactivate]").ClickAsync();
        await ConfirmAsync();
        await Expect(Page.Locator("[data-testid=admin-fund-status]").First).ToHaveTextAsync(new Regex("Activo"));
    }

    [Test]
    public async Task Admin_UploadsThenRemovesRegulation()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);
        var name = $"FondoReg-{u}";

        await CreateFundAsync(name, "Fondo con reglamento.");
        await OpenDetailsAsync(name);
        await Expect(Page.Locator("[data-testid=admin-fund-regulation-none]")).ToBeVisibleAsync();

        // Upload a valid PDF.
        await Page.Locator("[data-testid=admin-fund-regulation-upload-input]").SetInputFilesAsync(_pdfPath);
        await Page.Locator("[data-testid=admin-fund-regulation-upload-submit]").ClickAsync();
        await Expect(Page.Locator("[data-testid=admin-fund-regulation-present]")).ToBeVisibleAsync();

        // Remove (spec-024 confirm) → back to none.
        await Page.Locator("[data-testid=admin-fund-regulation-remove]").ClickAsync();
        await ConfirmAsync();
        await Expect(Page.Locator("[data-testid=admin-fund-regulation-none]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_BlankName_IsRejected()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);

        await Page.GotoAsync($"{BaseUrl}/Admin/Funds/Create");
        await Page.Locator("[data-testid=admin-fund-description-input]").FillAsync("Sin nombre.");
        await Page.Locator("[data-testid=admin-fund-create-submit]").ClickAsync();

        // Stays on Create with the required-name validation error.
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Funds/Create$"));
        await Expect(Page.Locator("[data-testid=admin-fund-name-error]")).ToContainTextAsync(new Regex("obligatorio"));
    }

    [Test]
    public async Task Admin_NonPdfRegulation_IsRejected()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);
        var name = $"FondoBad-{u}";

        await CreateFundAsync(name, "Fondo con archivo inválido.");
        await OpenDetailsAsync(name);
        await Page.Locator("[data-testid=admin-fund-regulation-upload-input]").SetInputFilesAsync(_notPdfPath);
        await Page.Locator("[data-testid=admin-fund-regulation-upload-submit]").ClickAsync();

        // Rejected — no regulation attached (error toast surfaces; regulation stays absent).
        await Expect(Page.Locator("[data-testid=admin-fund-regulation-none]")).ToBeVisibleAsync();
    }
}
