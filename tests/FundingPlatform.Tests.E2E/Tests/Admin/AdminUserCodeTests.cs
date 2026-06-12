using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 032 / US2 — admin assigns a required, unique User Code to each Solicitante.
/// Blank → blocked; duplicate → blocked; valid → created. The field is shown only
/// for the Applicant role, and the applicant sees the code read-only on /Profile.
/// </summary>
public class AdminUserCodeTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string TempUserPassword = "TempPass1!";

    private async Task SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"uc_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Code", "Admin", $"UCADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [Test]
    public async Task CreateSolicitante_BlankUserCode_IsBlocked()
    {
        await SignInAsAdminAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Blank", lastName: "Code", email: $"uc_blank_{unique}@example.com",
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"UCB-{unique}"));
        // Clear the auto-filled code so the required rule fires.
        await createPage.UserCode.FillAsync("");
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users/Create"));
        await Expect(Page.GetByText("El código de usuario es obligatorio para el rol Solicitante."))
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateSolicitante_DuplicateUserCode_IsBlocked()
    {
        await SignInAsAdminAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var code = $"DUP-{unique}";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "First", lastName: "Owner", email: $"uc_dup1_{unique}@example.com",
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"UCD1-{unique}"), userCode: code);
        await createPage.SubmitAsync();
        // Spec 033 — a successful create lands on the "Invitación enviada" confirmation.
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Second", lastName: "Clash", email: $"uc_dup2_{unique}@example.com",
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"UCD2-{unique}"), userCode: code);
        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users/Create"));
        await Expect(Page.GetByText("El código de usuario ya está en uso.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateSolicitante_ValidUserCode_Succeeds_AndAppearsInList()
    {
        await SignInAsAdminAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var code = $"OK-{unique}";
        var email = $"uc_ok_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Valid", lastName: "Code", email: email,
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"UCOK-{unique}"), userCode: code);
        await createPage.SubmitAsync();
        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();

        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(code);
        await Expect(list.RowFor(email)).ToBeVisibleAsync();
    }

    [Test]
    public async Task UserCodeField_ShownOnlyForSolicitante()
    {
        await SignInAsAdminAsync();

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);

        await createPage.Role.SelectOptionAsync("Applicant");
        Assert.That(await createPage.UserCodeField.IsVisibleAsync(), Is.True,
            "User Code field must be visible for the Solicitante role.");

        await createPage.Role.SelectOptionAsync("Reviewer");
        Assert.That(await createPage.UserCodeField.IsVisibleAsync(), Is.False,
            "User Code field must be hidden for non-applicant roles.");

        await createPage.Role.SelectOptionAsync("Admin");
        Assert.That(await createPage.UserCodeField.IsVisibleAsync(), Is.False,
            "User Code field must be hidden for the Admin role.");
    }

    [Test]
    public async Task NonApplicant_CreatedWithoutUserCode_Succeeds()
    {
        await SignInAsAdminAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"uc_rev_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Rev", lastName: "NoCode", email: email,
            phone: null, role: "Reviewer", initialPassword: TempUserPassword, legalId: null);
        await createPage.SubmitAsync();

        await Expect(new InvitationSentPage(Page).Root).ToBeVisibleAsync();
    }

    [Test]
    public async Task Applicant_SeesUserCodeReadOnly_OnProfile()
    {
        await SignInAsAdminAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var code = $"PROF-{unique}";
        var email = $"uc_prof_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Prof", lastName: "Applicant", email: email,
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"UCP-{unique}"), userCode: code);
        await createPage.SubmitAsync();

        // Spec 033 — onboard the applicant via the emailed set-password invitation,
        // then sign in (no forced change-password) to view their profile.
        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        var inviteLink = await sentPage.GetInviteLinkAsync();
        await Page.Locator("form[action*='Account/Logout'] button[type=submit]").ClickAsync();
        await SetPasswordViaInviteAsync(inviteLink, "NewPass1!");
        await LoginAsync(Page, email, "NewPass1!");

        // The self-service profile lives at /Profile (attribute-routed), not /Account/Profile.
        await Page.GotoAsync($"{BaseUrl}/Profile");
        var field = Page.Locator("[data-testid=\"profile-usercode\"]");
        await Expect(field).ToBeVisibleAsync();
        Assert.That(await field.InputValueAsync(), Is.EqualTo(code));
        Assert.That(await field.IsDisabledAsync(), Is.True, "Applicant must not be able to edit the code.");
    }
}
