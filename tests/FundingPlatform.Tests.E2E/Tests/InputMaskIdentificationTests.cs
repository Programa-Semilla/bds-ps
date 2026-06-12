using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 026 / US1 — type-aware identification masking on the admin user form:
/// client formats as typed, strips letters on numeric types, the saved type+value
/// round-trip on admin edit, and a malformed value (client mask removed) is
/// rejected server-side with an es-CR message.
///
/// Spec 032 — public self-registration was removed, so these masks are now
/// exercised on <c>/Admin/Users/Create</c> (the same <c>_LegalIdField</c> partial),
/// which is where the type-aware identification entry lives going forward.
/// </summary>
public class InputMaskIdentificationTests : AuthenticatedTestBase
{
    private async Task<AdminUserCreatePage> LoginAsAdminAndOpenCreateAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"mask_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, "Test123!", "Mask", "Admin", $"MADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, "Test123!");

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        // Default role is Applicant, so the identification type + masked value fields render.
        await createPage.Role.SelectOptionAsync("Applicant");
        return createPage;
    }

    [TestCase("CedulaFisica", "123456789", "1-2345-6789")]
    [TestCase("Dimex", "123456789012", "123456789012")]
    [TestCase("Pasaporte", "a1b2c3", "A1B2C3")]
    public async Task Mask_FormatsValueAsTyped(string type, string input, string expected)
    {
        var createPage = await LoginAsAdminAndOpenCreateAsync();

        await createPage.IdentificationTypeSelect.SelectOptionAsync(type);
        await createPage.LegalId.PressSequentiallyAsync(input);

        Assert.That(await createPage.LegalId.InputValueAsync(), Is.EqualTo(expected));
    }

    [Test]
    public async Task NumericMask_StripsLetters()
    {
        var createPage = await LoginAsAdminAndOpenCreateAsync();

        await createPage.IdentificationTypeSelect.SelectOptionAsync("CedulaFisica");
        await createPage.LegalId.PressSequentiallyAsync("12ab34");

        var value = await createPage.LegalId.InputValueAsync();
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
        // Spec 032 — User Code is required for Solicitante; fill a unique value.
        await createPage.FillUserCodeIfPresentAsync($"UC-{unique}");
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
    public async Task MalformedIdentification_RejectedServerSide_WithSpanishError()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"mask_bad_{unique}@example.com";

        var createPage = await LoginAsAdminAndOpenCreateAsync();
        await createPage.FirstName.FillAsync("Mask");
        await createPage.LastName.FillAsync("Bad");
        await createPage.Email.FillAsync(email);
        // Spec 033 — no password field on the create form anymore.
        await createPage.IdentificationTypeSelect.SelectOptionAsync("CedulaFisica");
        // Strip the client mask so a malformed value reaches the server unmodified
        // (FR-014 — the client mask is never trusted).
        await Page.EvalOnSelectorAsync("[name=LegalId]", "el => el.removeAttribute('data-mask')");
        await createPage.LegalId.FillAsync("ABCDEF");

        // Spec 016 — Applicant requires ≥1 group; select all so the only blocking
        // error is the malformed identification.
        var formPage = new AdminUserFormPage(Page);
        if (await formPage.GroupsField.IsVisibleAsync() && await formPage.GroupSelector.CountAsync() > 0)
        {
            await formPage.SelectAllGroupsAsync();
        }

        await createPage.SubmitAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/Admin/Users/Create"));
        await Expect(Page.GetByText("La identificación no tiene el formato de Cédula física."))
            .ToBeVisibleAsync();
    }
}
