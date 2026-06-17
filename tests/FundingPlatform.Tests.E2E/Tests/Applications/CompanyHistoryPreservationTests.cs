using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Applications;

/// <summary>
/// Spec 037 / US3 — the company name is snapshotted at creation and re-copied on
/// draft re-select (FR-015/016). Renaming a company never rewrites prior
/// applications' snapshots. The rename/archive are applied via SQL to isolate the
/// snapshot/freeze behavior (the admin rename UI is covered by AdminCompanyManagementTests).
/// </summary>
[Category("Applications")]
[Category("Spec037")]
public class CompanyHistoryPreservationTests : AuthenticatedTestBase
{
    [Test]
    public async Task Rename_PreservesPriorSnapshot_NewApplicationGetsNewName()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_hist_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Histo", $"Ria{uniqueId}", $"HIS-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);

        // Create app1 under the first company; capture its snapshotted name.
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();
        var chosenName = await appPage.CompanySelect.Locator("option").Nth(1).InnerTextAsync();
        await appPage.CompanySelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var app1Id = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        await Page.GotoAsync($"{BaseUrl}/Application/Details/{app1Id}");
        await Expect(Page.Locator("body")).ToContainTextAsync(chosenName.Trim());

        // Rename that company in the catalog.
        var newName = $"Renombrada {uniqueId}";
        await ExecSqlAsync(
            @"UPDATE dbo.Companies SET Name = @newName
              WHERE Name = @oldName AND ApplicantId =
                  (SELECT a.Id FROM dbo.Applicants a JOIN AspNetUsers u ON a.UserId = u.Id WHERE u.Email = @email)",
            ("@newName", newName), ("@oldName", chosenName.Trim()), ("@email", email));

        // app1's snapshot is frozen — Details still shows the OLD name.
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{app1Id}");
        await Expect(Page.Locator("body")).ToContainTextAsync(chosenName.Trim());

        // A new application under the (now renamed) company snapshots the NEW name.
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();
        await appPage.CompanySelect.SelectOptionAsync(new SelectOptionValue { Label = newName });
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var app2Id = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        await Page.GotoAsync($"{BaseUrl}/Application/Details/{app2Id}");
        await Expect(Page.Locator("body")).ToContainTextAsync(newName);
    }

    [Test]
    public async Task DraftReselect_UpdatesSnapshot()
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var email = $"co_resel_{uniqueId}@example.com";
        await RegisterUserAsync(Page, email, "Test123!", "Resel", $"Ect{uniqueId}", $"RES-{uniqueId}");
        await LoginAsync(Page, email, "Test123!");

        var appPage = new ApplicationPage(Page);
        await appPage.GotoListAsync(BaseUrl);
        await appPage.CreateButton.ClickAsync();
        await appPage.CompanySelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await appPage.SelectEligibleGroupIfPresentAsync();
        await appPage.SubmitDraftButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/Application/Edit/\d+"));
        var appId = int.Parse(Regex.Match(Page.Url, @"/Application/Edit/(\d+)").Groups[1].Value);

        // Re-select the OTHER company on the draft editor; capture its label.
        var draft = new ApplicationDraftPage(Page);
        var otherName = await draft.CompanySelect.Locator("option").Nth(2).InnerTextAsync();
        await draft.SelectCompanyByIndexAsync(2);
        await Expect(draft.AutosaveIndicator).ToHaveAttributeAsync("data-autosave-state", "saved");

        // The snapshot now reflects the re-selected company.
        await Page.GotoAsync($"{BaseUrl}/Application/Details/{appId}");
        await Expect(Page.Locator("body")).ToContainTextAsync(otherName.Trim());
    }

    private async Task ExecSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync();
    }
}
