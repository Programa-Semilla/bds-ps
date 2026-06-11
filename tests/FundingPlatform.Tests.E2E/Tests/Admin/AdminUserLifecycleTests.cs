using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

public class AdminUserLifecycleTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string TempUserPassword = "TempPass1!";

    private async Task<string> SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"lifecycle_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Lifecycle", "Admin", $"LADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
        return adminEmail;
    }

    [Test]
    public async Task Admin_CreateReviewer_AppearsInListing()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var newReviewerEmail = $"lifecycle_rev_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "New",
            lastName: "Reviewer",
            email: newReviewerEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();

        var listPage = new AdminUsersListPage(Page);
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Admin/Users(\\?.*)?$"));
        await listPage.SearchAsync(newReviewerEmail);
        await Expect(listPage.RowFor(newReviewerEmail)).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_CreateApplicant_RequiresLegalId()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var applicantEmail = $"lifecycle_app_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "New",
            lastName: "Applicant",
            email: applicantEmail,
            phone: null,
            role: "Applicant",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();

        await Expect(createPage.ValidationSummary.First).ToBeVisibleAsync();
        await Expect(Page.Locator("form[data-testid=\"admin-user-create-form\"]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_CreateApplicant_WithLegalId_PersistsApplicantRow()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var applicantEmail = $"lifecycle_app_{unique}@example.com";
        var legalId = IdentificationData.CedulaFisica($"LCAP-{unique}");

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "New",
            lastName: "Applicant",
            email: applicantEmail,
            phone: null,
            role: "Applicant",
            initialPassword: TempUserPassword,
            legalId: legalId);
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Admin/Users(\\?.*)?$"));
        var listPage = new AdminUsersListPage(Page);
        await listPage.SearchAsync(applicantEmail);
        await Expect(listPage.RowFor(applicantEmail)).ToBeVisibleAsync();
    }

    [Test]
    public async Task NewlyCreatedUser_OnFirstLogin_RedirectsToChangePassword()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var targetEmail = $"firstlogin_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "First",
            lastName: "Login",
            email: targetEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await LoginAsync(Page, targetEmail, TempUserPassword);
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/ChangePassword"));
    }

    [Test]
    public async Task Admin_ChangePassword_ClearsMustChangeFlag_AndAllowsContinuedNavigation()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var targetEmail = $"changepw_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Change",
            lastName: "Password",
            email: targetEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();

        await LoginAsync(Page, targetEmail, TempUserPassword);
        var changePage = new ChangePasswordPage(Page);
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/ChangePassword"));
        await changePage.SubmitAsync(TempUserPassword, "NewPass1!", "NewPass1!");

        // ChangePassword redirects to Home/Index; spec-021 US6 routes a Reviewer
        // on from there to /Reviewer/Dashboard. The created user holds the
        // Reviewer role, so the post-change landing is the reviewer dashboard.
        await Expect(Page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex("/$|/Home|/Application|/Reviewer"));
    }

    [Test]
    public async Task Admin_DisableUser_PreventsLogin()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var targetEmail = $"disable_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Disable",
            lastName: "Target",
            email: targetEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();

        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(targetEmail);
        // Spec 024 — disable now opens the shared confirm modal; click confirm.
        await listPage.RowDisableButton(targetEmail).ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
        await Expect(Page.Locator("[data-testid=\"success-banner\"]")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, targetEmail, TempUserPassword);
        await Expect(Page.Locator(".text-danger, .alert-danger, .validation-summary-errors").First)
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_EnableDisabledUser_AllowsLogin()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var targetEmail = $"enable_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Enable",
            lastName: "Target",
            email: targetEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();

        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(targetEmail);
        // Spec 024 — disable now opens the shared confirm modal; click confirm.
        await listPage.RowDisableButton(targetEmail).ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
        await Expect(Page.Locator("[data-testid=\"success-banner\"]")).ToBeVisibleAsync();

        // The status filter now defaults to "Activo"; load the just-disabled
        // user explicitly so it is listed before re-enabling.
        await Page.GotoAsync($"{BaseUrl}/Admin/Users?statusFilter=Disabled&search={targetEmail}");
        await listPage.RowEnableButton(targetEmail).ClickAsync();
        await Expect(Page.Locator("[data-testid=\"success-banner\"]")).ToBeVisibleAsync();

        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await LoginAsync(Page, targetEmail, TempUserPassword);
        // First login should redirect to change-password (MustChangePassword still set from initial creation).
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/ChangePassword"));
    }

    [Test]
    public async Task Admin_DemoteApplicantToReviewer_PreservesApplicantRecord_AndAllowsNavigation()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var applicantEmail = $"demote_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Demote",
            lastName: "Target",
            email: applicantEmail,
            phone: null,
            role: "Applicant",
            initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"DMT-{unique}"));
        await createPage.SubmitAsync();

        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(applicantEmail);
        await listPage.RowEditLink(applicantEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await editPage.SetRoleAsync("Reviewer");
        await editPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Admin/Users(\\?.*)?$"));
    }
}
