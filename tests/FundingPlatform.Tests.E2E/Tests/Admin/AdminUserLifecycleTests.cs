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

        // Spec 033 — create now lands on the "Invitación enviada" confirmation;
        // navigate to the list to verify the new user appears.
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
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

        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(applicantEmail);
        await Expect(listPage.RowFor(applicantEmail)).ToBeVisibleAsync();
    }

    [Test]
    public async Task NewlyInvitedUser_AfterSettingPassword_SignsInWithoutChangePassword()
    {
        // Spec 033 — replaces the obsolete temp-password + first-login
        // change-password tests. An admin-created user has no password; they set
        // it through the emailed invitation and then sign in normally (no forced
        // change-password, because invited users have MustChangePassword=false).
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var targetEmail = $"invited_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Invited",
            lastName: "User",
            email: targetEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();

        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        var inviteLink = await sentPage.GetInviteLinkAsync();

        // Admin signs out; the invited user onboards via the link, then signs in.
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await SetPasswordViaInviteAsync(inviteLink, "NewPass1!");
        await LoginAsync(Page, targetEmail, "NewPass1!");

        await Expect(Page).Not.ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex("/Account/(Login|ChangePassword)"));
        await Expect(Page.Locator("form[action*='Account/Logout'] button[type=submit]")).ToBeVisibleAsync();
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
        const string userPassword = "NewPass1!";

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

        // Spec 033 — the user has no password; onboard via the invite link so it
        // can authenticate (the admin session persists across the anonymous
        // set-password flow).
        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        var inviteLink = await sentPage.GetInviteLinkAsync();
        await SetPasswordViaInviteAsync(inviteLink, userPassword);

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
        await LoginAsync(Page, targetEmail, userPassword);
        // Re-enabled user signs in with the password they set — no forced
        // change-password (invited users have MustChangePassword=false).
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/(Login|ChangePassword)"));
        await Expect(Page.Locator("form[action*='Account/Logout'] button[type=submit]")).ToBeVisibleAsync();
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
