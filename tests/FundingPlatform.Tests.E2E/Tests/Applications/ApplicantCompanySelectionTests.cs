using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Applications;

/// <summary>
/// Spec 037 / US1 — the applicant selects an admin-assigned company on creation.
/// Covers the 0/1/many rendering (FR-012–FR-014), server-side validation
/// (FR-018/019), and the frozen name snapshot. The seeded applicant
/// (RegisterUserAsync → /Account/SeedUser) gets two active companies, so SQL is
/// used to derive the single-company and zero-company variants.
/// </summary>
[Category("Applications")]
[Category("Spec037")]
public class ApplicantCompanySelectionTests : AuthenticatedTestBase
{
    [Test]
    public async Task MultiCompany_RequiresChoice()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_req_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Req", "Empresa", $"REQ-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        // Two companies → a real <select> with no default; submitting without a
        // choice surfaces the required-company error and does not create a draft.
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));
        await Expect(appPage.CompanyError).ToContainTextAsync("empresa");
    }

    [Test]
    public async Task MultiCompany_Select_CreatesWithSnapshot()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_multi_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Multi", "Empresa", $"MUL-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        await appPage.SelectCompanyIfPresentAsync();
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        // The selected company's name is snapshotted and shows on Details.
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");
        await Expect(Page.Locator("body")).ToContainTextAsync("Empresa Multi Empresa");
    }

    [Test]
    public async Task SingleCompany_AutoSelects_AndCreatesWithoutChoosing()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_single_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Single", "Empresa", $"SIN-{uniqueId}");
        // Archive one of the two seeded companies → exactly one active company.
        await ExecSqlAsync(
            @"UPDATE dbo.Companies SET ArchivedAt = SYSUTCDATETIME()
              WHERE Id = (SELECT TOP 1 c.Id FROM dbo.Companies c
                          JOIN dbo.Applicants a ON c.ApplicantId = a.Id
                          JOIN AspNetUsers u ON a.UserId = u.Id
                          WHERE u.Email = @email ORDER BY c.Id)",
            email);
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        // Single company → hidden auto-selected field; no <select> to pick.
        await Expect(Page.Locator("[data-testid=application-create-company-readonly]")).ToBeVisibleAsync();
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
    }

    [Test]
    public async Task ZeroCompanies_BlocksCreation()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_zero_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Zero", "Empresa", $"ZER-{uniqueId}");
        await ExecSqlAsync(
            @"DELETE c FROM dbo.Companies c
              JOIN dbo.Applicants a ON c.ApplicantId = a.Id
              JOIN AspNetUsers u ON a.UserId = u.Id
              WHERE u.Email = @email",
            email);
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        await Expect(appPage.NoCompaniesBlock).ToBeVisibleAsync();
        await Expect(appPage.SubmitDraftButton).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ForgedCompanyId_RejectedServerSide()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_forge_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Forge", "Empresa", $"FOR-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();

        // Tamper: inject a bogus option value into the company <select> and submit.
        // The server re-validates the posted CompanyId against the active set (FR-018).
        await appPage.CompanySelect.EvaluateAsync(
            "el => { const o = document.createElement('option'); o.value = '999999'; o.selected = true; el.appendChild(o); }");
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Create"));
        await Expect(appPage.CompanyError).ToContainTextAsync("empresa");
    }

    private async Task ExecSqlAsync(string sql, string email)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@email", email);
        await cmd.ExecuteNonQueryAsync();
    }
}
