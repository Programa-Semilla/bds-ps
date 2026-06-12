using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 033 — admin creates a user with no password; the user onboards via an
/// emailed 72h single-use set-password invitation. The confirmation screen shows
/// a copyable link (FR-008 delivery-resilience fallback), so these tests onboard
/// through that admin-visible link rather than scraping the email — which is also
/// the realistic non-prod path, where the allowlist may drop the email recipient.
/// </summary>
public class UserInvitationTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string NewUserPassword = "NewPass1!";

    private async Task SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"invite_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Invite", "Admin", $"IADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    /// <summary>Asserts we landed authenticated (not bounced to Login / ChangePassword).</summary>
    private async Task AssertSignedInAsync()
    {
        await Expect(Page).Not.ToHaveURLAsync(new Regex("/Account/(Login|ChangePassword)"));
        await Expect(Page.Locator("form[action*='Account/Logout'] button[type=submit]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateApplicant_SendsInvitation_UserSetsPasswordAndSignsIn()
    {
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var email = $"invite_app_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);

        // FR-001 — there is no password field on the create form.
        await Expect(Page.Locator("input[name=\"InitialPassword\"]")).ToHaveCountAsync(0);

        await createPage.FillAsync(
            firstName: "Invited",
            lastName: "Applicant",
            email: email,
            phone: null,
            role: "Applicant",
            initialPassword: "ignored",
            legalId: IdentificationData.CedulaFisica($"INV-{unique}"));
        await createPage.SubmitAsync();

        // C1/C5 — confirmation with the recipient email and a copyable invite link.
        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        await Expect(sentPage.Headline).ToContainTextAsync(email);
        var inviteLink = await sentPage.GetInviteLinkAsync();
        Assert.That(inviteLink, Does.Contain("/Account/ResetPassword"));

        // Onboard via the link, then sign in — no forced change-password (FR-005).
        await SetPasswordViaInviteAsync(inviteLink, NewUserPassword);
        await LoginAsync(Page, email, NewUserPassword);
        await AssertSignedInAsync();
    }

    [TestCase("Admin")]
    [TestCase("SupplierAdmin")]
    public async Task CreateStaffRole_SendsInvitation_UserSetsPasswordAndSignsIn(string role)
    {
        // FR-003 — the same invitation onboarding applies to every admin-created
        // role. Applicant + Reviewer are covered by the dedicated tests above;
        // this covers Administrador + Administrador de proveedores.
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var email = $"invite_{role.ToLowerInvariant()}_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await Expect(Page.Locator("input[name=\"InitialPassword\"]")).ToHaveCountAsync(0);
        await createPage.FillAsync(
            firstName: "Invited",
            lastName: role,
            email: email,
            phone: null,
            role: role,
            initialPassword: "ignored",
            legalId: null);
        await createPage.SubmitAsync();

        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        var inviteLink = await sentPage.GetInviteLinkAsync();

        await SetPasswordViaInviteAsync(inviteLink, NewUserPassword);
        await LoginAsync(Page, email, NewUserPassword);
        await AssertSignedInAsync();
    }

    [Test]
    public async Task InvalidLink_ShowsEsCrRejection_AndSetsNoPassword()
    {
        // FR-010 — a tampered/invalid invitation link shows the es-CR rejection
        // and changes no password. Anonymous; no admin needed.
        await Page.GotoAsync($"{BaseUrl}/Account/ResetPassword?userId=bogus-user-id&token=bogus-token");

        var reset = new ResetPasswordPage(Page);
        await Expect(reset.InvalidLinkMessage).ToBeVisibleAsync();
        await Expect(reset.InvalidLinkMessage).ToContainTextAsync("Enlace inválido o expirado");
        // The set-password form must not be presented for an invalid link.
        await Expect(reset.FormRoot).ToHaveCountAsync(0);
    }

    [Test]
    public async Task CreateReviewer_SendsInvitation_UserSetsPasswordAndSignsIn()
    {
        // FR-003 — onboarding works for staff roles too.
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var email = $"invite_rev_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Invited",
            lastName: "Reviewer",
            email: email,
            phone: null,
            role: "Reviewer",
            initialPassword: "ignored",
            legalId: null);
        await createPage.SubmitAsync();

        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        var inviteLink = await sentPage.GetInviteLinkAsync();

        await SetPasswordViaInviteAsync(inviteLink, NewUserPassword);
        await LoginAsync(Page, email, NewUserPassword);
        await AssertSignedInAsync();
    }

    [Test]
    public async Task ResendInvitation_SupersedesPriorLink_NewLinkOnboards()
    {
        // US2 / C3 — resend issues a fresh link and invalidates the prior unused one.
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var email = $"invite_resend_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Resend",
            lastName: "Target",
            email: email,
            phone: null,
            role: "Reviewer",
            initialPassword: "ignored",
            legalId: null);
        await createPage.SubmitAsync();

        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        var firstLink = await sentPage.GetInviteLinkAsync();

        // Resend from the users list.
        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(email);
        await listPage.RowResendInviteButton(email).ClickAsync();
        await Expect(sentPage.Root).ToBeVisibleAsync();
        var secondLink = await sentPage.GetInviteLinkAsync();
        Assert.That(secondLink, Is.Not.EqualTo(firstLink), "Resend must issue a different link.");

        // The first (superseded) link is now rejected on submit.
        await Page.GotoAsync(firstLink);
        var reset = new ResetPasswordPage(Page);
        await Expect(reset.FormRoot).ToBeVisibleAsync();
        await reset.SubmitAsync(NewUserPassword, NewUserPassword);
        await Expect(reset.ValidationSummary).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Account/ResetPassword"));

        // The new link completes onboarding.
        await SetPasswordViaInviteAsync(secondLink, NewUserPassword);
        await LoginAsync(Page, email, NewUserPassword);
        await AssertSignedInAsync();
    }

    [Test]
    public async Task Confirmation_ExposesCopyableWorkingLink_AsDeliveryFallback()
    {
        // US3 / FR-008 — the copyable admin-visible link onboards even when the
        // email is undeliverable / allowlist-dropped (we never read any email here).
        await SignInAsAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var email = $"invite_fallback_{unique}@example.com";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Fallback",
            lastName: "Target",
            email: email,
            phone: null,
            role: "Reviewer",
            initialPassword: "ignored",
            legalId: null);
        await createPage.SubmitAsync();

        var sentPage = new InvitationSentPage(Page);
        await Expect(sentPage.Root).ToBeVisibleAsync();
        await Expect(sentPage.InviteLinkInput).ToBeVisibleAsync();
        await Expect(sentPage.CopyButton).ToBeVisibleAsync();
        var inviteLink = await sentPage.GetInviteLinkAsync();
        Assert.That(inviteLink, Does.Contain("/Account/ResetPassword"));

        await SetPasswordViaInviteAsync(inviteLink, NewUserPassword);
        await LoginAsync(Page, email, NewUserPassword);
        await AssertSignedInAsync();
    }
}
