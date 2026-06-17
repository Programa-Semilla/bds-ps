using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 037 / US2 — admin creates a Solicitante with ≥1 company, then adds /
/// renames / archives via the Edit-page "Empresas" card, and is blocked from
/// archiving the last active company (FR-008). Also asserts the create form
/// requires at least one company.
/// </summary>
[Category("Admin")]
[Category("Spec037")]
public class AdminCompanyManagementTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string TempUserPassword = "TempPass1!";

    private async Task SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"co_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Company", "Admin", $"COADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [Test]
    public async Task CreateSolicitante_WithoutCompany_IsBlocked()
    {
        await SignInAsAdminAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Sin", lastName: "Empresa", email: $"co_none_{unique}@example.com",
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"CON-{unique}"));
        // Clear the auto-filled company so the ≥1 rule fires.
        await createPage.CompanyInputs.First.FillAsync("");
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users/Create"));
        await Expect(createPage.CompaniesError).ToContainTextAsync("al menos una empresa");
    }

    [Test]
    public async Task ManageCompanies_Add_Rename_Archive_LastActiveBlocked()
    {
        await SignInAsAdminAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_mgmt_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Gestión", lastName: "Empresas", email: email,
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"MGM-{unique}"));
        await createPage.CompanyInputs.First.FillAsync("Empresa Uno");
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        // Navigate to the new user's Edit page via the list.
        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.RowEditLink(email).ClickAsync();

        var companies = new AdminUserCompaniesPage(Page);
        await Expect(companies.Card).ToBeVisibleAsync();
        await Expect(companies.Rows).ToHaveCountAsync(1);

        // Add a second company.
        await companies.AddCompanyAsync("Empresa Dos");
        await Expect(companies.Rows).ToHaveCountAsync(2);

        // Rename the first.
        await companies.RenameCompanyAsync("Empresa Uno", "Empresa Renombrada");
        await Expect(companies.RowFor("Empresa Renombrada")).ToBeVisibleAsync();

        // Archive the second — succeeds (one active remains).
        await companies.ArchiveCompanyAsync("Empresa Dos");
        await Expect(companies.RowFor("Empresa Dos")
            .Locator("[data-testid=\"admin-user-company-archived\"]")).ToBeVisibleAsync();

        // Archiving the last active company is blocked (FR-008).
        await companies.ArchiveCompanyAsync("Empresa Renombrada");
        await Expect(Page.GetByText("No puede archivar la única empresa activa del solicitante."))
            .ToBeVisibleAsync();
        await Expect(companies.RowFor("Empresa Renombrada")
            .Locator("[data-testid=\"admin-user-company-active\"]")).ToBeVisibleAsync();
    }
}
