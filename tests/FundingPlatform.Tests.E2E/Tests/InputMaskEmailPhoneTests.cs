using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 026 / US3 — email and CR-phone masks are consistent on every form that
/// renders them. Asserted on the admin user-create form (email + phone both
/// present and masked).
/// </summary>
public class InputMaskEmailPhoneTests : AuthenticatedTestBase
{
    private async Task SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"mask_emailphone_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, "Test123!", "Mask", "Admin", $"MEP-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, "Test123!");
    }

    [Test]
    public async Task EmailMask_FlagsInvalidOnBlur()
    {
        await SignInAsAdminAsync();

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);

        await createPage.Email.FillAsync("not-an-email");
        // Blur the email field by focusing another control.
        await createPage.Phone.ClickAsync();

        await Expect(Page.GetByText("Ingrese un correo electrónico válido.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task PhoneMask_FormatsToDashedGroups()
    {
        await SignInAsAdminAsync();

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);

        await createPage.Phone.PressSequentiallyAsync("88888888");

        Assert.That(await createPage.Phone.InputValueAsync(), Is.EqualTo("8888-8888"));
    }
}
