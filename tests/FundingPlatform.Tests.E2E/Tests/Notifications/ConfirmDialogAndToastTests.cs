using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Notifications;

/// <summary>
/// Spec 024 — net-new behaviours of the unified notification system that the
/// adapted suite does not otherwise assert: confirm-modal CANCEL aborts with no
/// side effect (US2 / SC-004), the success toast auto-dismisses while staying
/// announced (US1 / US5 / SC-003 / SC-006), and the modal carries the configured
/// es-CR copy (FR-007). Exercised through the admin "Inhabilitar usuario" flow.
/// </summary>
public class ConfirmDialogAndToastTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string TempUserPassword = "TempPass1!";

    private ILocator SharedConfirmModal => Page.Locator("#fl-shared-confirm-modal");
    private ILocator SharedConfirmButton => Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]");
    private ILocator SharedConfirmCancel => Page.Locator("#fl-shared-confirm-modal [data-testid=\"cancel-button\"]");
    private ILocator SuccessToast => Page.Locator("[data-testid=\"success-banner\"]");

    private async Task<string> SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"toast_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Toast", "Admin", $"TADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
        return adminEmail;
    }

    private async Task<(AdminUsersListPage list, string email)> SeedTargetAndOpenListAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var targetEmail = $"toast_target_{unique}@example.com";
        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Toast",
            lastName: "Target",
            email: targetEmail,
            phone: null,
            role: "Reviewer",
            initialPassword: TempUserPassword,
            legalId: null);
        await createPage.SubmitAsync();

        var listPage = new AdminUsersListPage(Page);
        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users(\\?.*)?$"));
        await listPage.SearchAsync(targetEmail);
        await Expect(listPage.RowFor(targetEmail)).ToBeVisibleAsync();
        return (listPage, targetEmail);
    }

    [Test]
    public async Task ConfirmModal_Cancel_AbortsWithNoSideEffect()
    {
        await SignInAsAdminAsync();
        var (listPage, targetEmail) = await SeedTargetAndOpenListAsync();

        // Open the styled confirm modal.
        await listPage.RowDisableButton(targetEmail).ClickAsync();
        await Expect(SharedConfirmModal).ToBeVisibleAsync();
        // FR-007 — configured es-CR copy.
        await Expect(Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-title\"]"))
            .ToHaveTextAsync("Inhabilitar usuario");

        // Cancel → modal closes, action does NOT run (FR-006 / SC-004).
        await SharedConfirmCancel.ClickAsync();
        await Expect(SharedConfirmModal).Not.ToBeVisibleAsync();

        // The user is still active (Disable still offered, not Enable).
        await Expect(listPage.RowDisableButton(targetEmail)).ToBeVisibleAsync();
    }

    [Test]
    public async Task ConfirmModal_Confirm_Proceeds_SuccessToast_IsAnnounced_AndAutoDismisses()
    {
        await SignInAsAdminAsync();
        var (listPage, targetEmail) = await SeedTargetAndOpenListAsync();

        await listPage.RowDisableButton(targetEmail).ClickAsync();
        await SharedConfirmButton.ClickAsync();

        // Success toast appears and is announced politely (FR-013 / SC-006).
        await Expect(SuccessToast).ToBeVisibleAsync();
        await Expect(SuccessToast).ToHaveAttributeAsync("aria-live", "polite");

        // FR-004 — the success toast auto-dismisses (~5s; well within the 15s timeout).
        await Expect(SuccessToast).Not.ToBeVisibleAsync();

        // The action proceeded. The post-disable redirect drops the search filter,
        // so re-search before asserting the row flipped to "Enable" (robust under
        // shared-fixture load where the unfiltered list spans many pages).
        await listPage.SearchAsync(targetEmail);
        await Expect(listPage.RowEnableButton(targetEmail)).ToBeVisibleAsync();
    }
}
