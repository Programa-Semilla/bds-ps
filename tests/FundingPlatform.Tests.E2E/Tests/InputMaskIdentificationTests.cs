using System.Linq;
using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 026 / US1 — type-aware identification masking on the Register and admin
/// user forms: client formats as typed, strips letters on numeric types, the saved
/// type+value round-trip on admin edit, and a malformed value (client mask removed)
/// is rejected server-side with an es-CR message.
/// </summary>
public class InputMaskIdentificationTests : AuthenticatedTestBase
{
    [TestCase("CedulaFisica", "123456789", "1-2345-6789")]
    [TestCase("Dimex", "123456789012", "123456789012")]
    [TestCase("Pasaporte", "a1b2c3", "A1B2C3")]
    public async Task Register_Mask_FormatsValueAsTyped(string type, string input, string expected)
    {
        var registerPage = new RegisterPage(Page);
        await registerPage.GotoAsync(BaseUrl);

        await registerPage.IdentificationTypeSelect.SelectOptionAsync(type);
        await registerPage.LegalIdInput.PressSequentiallyAsync(input);

        Assert.That(await registerPage.LegalIdInput.InputValueAsync(), Is.EqualTo(expected));
    }

    [Test]
    public async Task Register_NumericMask_StripsLetters()
    {
        var registerPage = new RegisterPage(Page);
        await registerPage.GotoAsync(BaseUrl);

        await registerPage.IdentificationTypeSelect.SelectOptionAsync("CedulaFisica");
        await registerPage.LegalIdInput.PressSequentiallyAsync("12ab34");

        var value = await registerPage.LegalIdInput.InputValueAsync();
        Assert.That(value, Does.Not.Match("[A-Za-z]"), "Numeric masks must drop letters.");
        Assert.That(value, Is.EqualTo("1-234"));
    }

    [Test]
    public async Task AdminCreateApplicant_EditRoundTripsTypeAndValue()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"mask_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, "Test123!", "Mask", "Admin", $"MADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, "Test123!");

        var applicantEmail = $"mask_app_{unique}@example.com";
        var dimex = IdentificationData.Dimex($"MAPP-{unique}");

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Mask",
            lastName: "Applicant",
            email: applicantEmail,
            phone: null,
            role: "Applicant",
            initialPassword: "TempPass1!",
            legalId: dimex,
            identificationType: "Dimex");
        await createPage.SubmitAsync();

        var listPage = new AdminUsersListPage(Page);
        await listPage.GoToAsync(BaseUrl);
        await listPage.SearchAsync(applicantEmail);
        await listPage.RowEditLink(applicantEmail).ClickAsync();

        var editPage = new AdminUserEditPage(Page);
        await Expect(editPage.LegalId).ToBeVisibleAsync();
        Assert.That(await editPage.GetSelectedIdentificationTypeAsync(), Is.EqualTo("Dimex"),
            "Edit form must pre-select the saved identification type.");
        Assert.That(await editPage.GetLegalIdValueAsync(), Is.EqualTo(dimex),
            "Edit form must show the saved canonical identification value.");
    }

    [Test]
    public async Task Register_MalformedIdentification_RejectedServerSide_WithSpanishError()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"mask_bad_{unique}@example.com";

        await Page.GotoAsync($"{BaseUrl}/Account/Register");
        await Page.FillAsync("[name=Email]", email);
        await Page.FillAsync("[name=Password]", "Test123!");
        await Page.FillAsync("[name=ConfirmPassword]", "Test123!");
        await Page.FillAsync("[name=FirstName]", "Mask");
        await Page.FillAsync("[name=LastName]", "Bad");
        // Type defaults to Cédula física. Strip the client mask so a malformed value
        // reaches the server unmodified (FR-014 — the client mask is never trusted).
        await Page.EvalOnSelectorAsync("[name=LegalId]", "el => el.removeAttribute('data-mask')");
        await Page.FillAsync("[name=LegalId]", "ABCDEF");
        await Page.Locator("form[action*='Account/Register'] button[type=submit]").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Account/Register"));
        await Expect(Page.GetByText("La identificación no tiene el formato de Cédula física."))
            .ToBeVisibleAsync();
    }
}
