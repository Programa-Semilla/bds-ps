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
    public ILocator LegalId => Page.Locator("input[name=\"LegalId\"]");
    public ILocator LegalIdField => Page.Locator("[data-testid=\"legalid-field\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"admin-user-create-submit\"]");
    public ILocator ValidationSummary => Page.Locator(".validation-summary-errors, .field-validation-error");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Users/Create");

    public async Task FillAsync(
        string firstName,
        string lastName,
        string email,
        string? phone,
        string role,
        string initialPassword,
        string? legalId)
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
            await LegalId.FillAsync(legalId);
        }

        // Spec 016 / FR-008 — every Applicant or Reviewer MUST have at least
        // one group. Existing pre-016 tests pass only the basic fields; the
        // POM defaults to selecting all visible groups so legacy callers stay
        // green. Tests that need to assert group-scoped behavior call
        // SelectGroupsAsync explicitly *after* this and overwrite the default.
        if (!string.Equals(role, "Admin", System.StringComparison.Ordinal))
        {
            var formPage = new AdminUserFormPage(Page);
            if (await formPage.GroupsSelect.CountAsync() > 0)
            {
                var allValues = await formPage.GroupsSelect.EvaluateAsync<string[]>(
                    "el => Array.from(el.options).map(o => o.value)");
                if (allValues is { Length: > 0 })
                {
                    var optionValues = allValues
                        .Select(v => new SelectOptionValue { Value = v })
                        .ToArray();
                    await formPage.GroupsSelect.SelectOptionAsync(optionValues);
                }
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
