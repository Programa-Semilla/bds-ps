using System.Linq;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

public class AdminUserCreatePage : AdminBasePage
{
    public AdminUserCreatePage(IPage page) : base(page)
    {
    }

    public ILocator FirstName => Page.Locator("input[name=\"FirstName\"]");
    public ILocator LastName => Page.Locator("input[name=\"LastName\"]");
    public ILocator Email => Page.Locator("input[name=\"Email\"]");
    public ILocator Phone => Page.Locator("input[name=\"Phone\"]");
    public ILocator Role => Page.Locator("select[name=\"Role\"]");
    public ILocator InitialPassword => Page.Locator("input[name=\"InitialPassword\"]");
    // Spec 026 — identification type selector + masked value input.
    public ILocator IdentificationTypeSelect => Page.Locator("select[name=\"IdentificationType\"]");
    public ILocator LegalId => Page.Locator("input[name=\"LegalId\"]");
    public ILocator LegalIdField => Page.Locator("[data-testid=\"legalid-field\"]");
    // Spec 032 — admin-assigned User Code (Solicitante only).
    public ILocator UserCode => Page.Locator("input[name=\"UserCode\"]");
    public ILocator UserCodeField => Page.Locator("[data-testid=\"admin-user-usercode\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"admin-user-create-submit\"]");
    public ILocator ValidationSummary => Page.Locator(".validation-summary-errors, .field-validation-error");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Users/Create");

    /// <summary>
    /// Spec 032 — fills the User Code input when it is rendered (Solicitante role).
    /// No-op for roles whose User Code field is JS-hidden / absent.
    /// </summary>
    public async Task FillUserCodeIfPresentAsync(string userCode)
    {
        if (await UserCode.CountAsync() > 0 && await UserCode.IsVisibleAsync())
        {
            await UserCode.FillAsync(userCode);
        }
    }

    public async Task FillAsync(
        string firstName,
        string lastName,
        string email,
        string? phone,
        string role,
        string initialPassword,
        string? legalId,
        string identificationType = "CedulaFisica",
        string? userCode = null)
    {
        await FirstName.FillAsync(firstName);
        await LastName.FillAsync(lastName);
        await Email.FillAsync(email);
        if (phone is not null)
        {
            await Phone.FillAsync(phone);
        }
        await Role.SelectOptionAsync(role);
        await InitialPassword.FillAsync(initialPassword);
        if (legalId is not null)
        {
            // Spec 026 — select the type then fill the masked value.
            await IdentificationTypeSelect.SelectOptionAsync(identificationType);
            await LegalId.FillAsync(legalId);
        }

        // Spec 032 / FR-008-analogue — User Code is required for Solicitante. Like the
        // group auto-select below, default a unique code so legacy admin-create-applicant
        // callers stay green; tests asserting code behavior pass an explicit value.
        if (string.Equals(role, "Applicant", System.StringComparison.Ordinal))
        {
            await FillUserCodeIfPresentAsync(userCode ?? $"UC-{Guid.NewGuid():N}"[..12]);
        }

        // Spec 016 / FR-008 — every Applicant or Reviewer MUST have at least
        // one group. Existing pre-016 tests pass only the basic fields; the
        // POM defaults to selecting all groups in the Fondo → Proceso → Grupo
        // drill-down so legacy callers stay green. Tests that need to assert
        // group-scoped behavior call SelectGroupsAsync explicitly *after* this
        // and add to the default. Skipped for the groupless roles (Admin /
        // SupplierAdmin) whose group field is JS-hidden.
        if (!string.Equals(role, "Admin", System.StringComparison.Ordinal))
        {
            var formPage = new AdminUserFormPage(Page);
            if (await formPage.GroupsField.IsVisibleAsync()
                && await formPage.GroupSelector.CountAsync() > 0)
            {
                await formPage.SelectAllGroupsAsync();
            }
        }
    }

    public async Task SubmitAsync()
    {
        // Scroll the button into view before clicking. Under tall forms + the
        // brand sponsor-strip footer the submit can land below the viewport,
        // and Playwright's auto-scroll heuristic occasionally reports the
        // element as still off-screen — observed flake in CI runs.
        await SubmitButton.ScrollIntoViewIfNeededAsync();
        await SubmitButton.ClickAsync();
    }
}
