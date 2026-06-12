using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using FundingPlatform.Tests.E2E.PageObjects.Admin.Reports;
using FundingPlatform.Tests.E2E.Support;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 032 / US3 — the widened search matches the applicant's User Code on the
/// admin users list (FR-012) and the applicants report (FR-014), and the code is
/// surfaced as a column on both (FR-016). The reviewer queue and the
/// Applications/Aging reports match on the same <c>Applicant.UserCode</c> column
/// through the identical EF LIKE predicate (match-only, no column).
/// </summary>
public class UserCodeSearchTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";
    private const string TempUserPassword = "TempPass1!";

    private async Task SignInAsAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"ucs_admin_{unique}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Search", "Admin", $"UCSADM-{unique}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    private async Task<(string email, string code)> CreateApplicantWithCodeAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"ucs_app_{unique}@example.com";
        var code = $"SRCH-{unique}";

        var createPage = new AdminUserCreatePage(Page);
        await createPage.GoToAsync(BaseUrl);
        await createPage.FillAsync(
            firstName: "Search", lastName: "Target", email: email,
            phone: null, role: "Applicant", initialPassword: TempUserPassword,
            legalId: IdentificationData.CedulaFisica($"UCS-{unique}"), userCode: code);
        await createPage.SubmitAsync();
        return (email, code);
    }

    [Test]
    public async Task AdminUsersList_SearchByUserCode_ReturnsApplicant_AndColumnShowsCode()
    {
        await SignInAsAdminAsync();
        var (email, code) = await CreateApplicantWithCodeAsync();

        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync(code);

        var row = list.RowFor(email);
        await Expect(row).ToBeVisibleAsync();
        var codeCell = row.Locator("[data-testid=\"admin-user-row-usercode\"]");
        Assert.That(await codeCell.InnerTextAsync(), Does.Contain(code),
            "The admin users list must show the User Code column for the applicant.");
    }

    [Test]
    public async Task AdminUsersList_SearchByUnrelatedTerm_ExcludesApplicant()
    {
        await SignInAsAdminAsync();
        var (email, _) = await CreateApplicantWithCodeAsync();

        var list = new AdminUsersListPage(Page);
        await list.GoToAsync(BaseUrl);
        await list.SearchAsync("zzz-no-such-code-zzz");

        Assert.That(await list.RowFor(email).CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task ApplicantsReport_SearchByUserCode_ReturnsApplicant_AndColumnShowsCode()
    {
        await SignInAsAdminAsync();
        var (_, code) = await CreateApplicantWithCodeAsync();

        var report = new AdminReportsApplicantsPage(Page);
        await report.GoToAsync(BaseUrl);
        await report.SearchInput.FillAsync(code);
        await report.ApplyButton.ClickAsync();

        await Expect(report.Rows.First).ToBeVisibleAsync();
        var codeCell = report.Rows.First.Locator("[data-testid=\"applicants-row-usercode\"]");
        Assert.That(await codeCell.InnerTextAsync(), Does.Contain(code),
            "The applicants report must match by User Code and show it as a column.");
    }
}
