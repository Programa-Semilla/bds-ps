// Spec 030 / US1 (T006) — inline admin rename of a Process Name on the detail page.

using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects.Admin;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.Admin;

/// <summary>
/// Spec 030 / US1 — an admin renames a Process in place on /Admin/Processes/{id}.
/// Covers: rename an Active Process (new name on header + Index + success toast);
/// rename a Closed Process; duplicate name rejected inline; empty name rejected
/// inline. Drives the real journey via the seeded "Fondo General" active Fund.
/// </summary>
public class RenameProcessTests : AuthenticatedTestBase
{
    private const string AdminPassword = "Test123!";

    private async Task SignInAsAdminAsync(string suffix)
    {
        var adminEmail = $"renadmin_{suffix}@example.com";
        await RegisterUserAsync(Page, adminEmail, AdminPassword, "Rename", "Admin", $"RADM-{suffix}");
        await AssignRoleAsync(adminEmail, "Admin");
        await LoginAsync(Page, adminEmail, AdminPassword);
    }

    [Test]
    public async Task Rename_ActiveProcess_UpdatesHeaderAndIndex_AndShowsSuccessToast()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);

        var original = $"Crocus 2025 {u}";
        var renamed = $"Crocus 2025-II {u}";

        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(original);
        await Expect(procPage.ProcessRow(original)).ToBeVisibleAsync();

        var id = await procPage.OpenProcessDetailByNameAsync(BaseUrl, original);
        await Expect(procPage.RenameForm).ToBeVisibleAsync();

        await procPage.RenameAsync(renamed);

        // Redirects back to the detail page; the new name is reflected.
        await Expect(Page).ToHaveURLAsync(new Regex($"/Admin/Processes/{id}$"));
        await Expect(procPage.FlashMessageDetail).ToContainTextAsync("Nombre del proceso actualizado.");
        await Expect(procPage.RenameInput).ToHaveValueAsync(renamed);
        await Expect(procPage.DetailsArea).ToContainTextAsync(renamed);

        // New name on the Processes index; old name gone.
        await procPage.GoToIndexAsync(BaseUrl);
        await Expect(procPage.ProcessRow(renamed)).ToBeVisibleAsync();
        await Expect(procPage.ProcessRow(original)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Rename_ClosedProcess_Succeeds()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);

        var original = $"Nexo Cerrado {u}";
        var renamed = $"Nexo Cerrado-II {u}";

        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(original);
        var id = await procPage.OpenProcessDetailByNameAsync(BaseUrl, original);

        // Close the (group-less, application-less) Process.
        await procPage.CloseAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/Admin/Processes/{id}$"));

        // FR-002 — the rename card is still available on a Closed Process.
        await Expect(procPage.RenameForm).ToBeVisibleAsync();
        await procPage.RenameAsync(renamed);

        await Expect(procPage.FlashMessageDetail).ToContainTextAsync("Nombre del proceso actualizado.");
        await Expect(procPage.RenameInput).ToHaveValueAsync(renamed);
        await Expect(procPage.DetailsArea).ToContainTextAsync(renamed);
    }

    [Test]
    public async Task Rename_DuplicateName_ShowsInlineError_NameUnchanged()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);

        var nameA = $"Proc A {u}";
        var nameB = $"Proc B {u}";

        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(nameA);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(nameB);

        // Try to rename A → B (B already exists) → inline duplicate error.
        await procPage.OpenProcessDetailByNameAsync(BaseUrl, nameA);
        await procPage.RenameAsync(nameB);

        await Expect(procPage.RenameError).ToContainTextAsync("Ya existe un proceso con ese nombre.");
        // A keeps its name (the form re-renders from the DB value).
        await Expect(procPage.RenameInput).ToHaveValueAsync(nameA);

        await procPage.GoToIndexAsync(BaseUrl);
        await Expect(procPage.ProcessRow(nameA)).ToBeVisibleAsync();
    }

    [Test]
    public async Task Rename_EmptyName_ShowsInlineError_NameUnchanged()
    {
        var u = Guid.NewGuid().ToString("N")[..6];
        await SignInAsAdminAsync(u);

        var original = $"Proc Vacio {u}";

        var procPage = new ProcessAdminPage(Page);
        await procPage.GoToCreateAsync(BaseUrl);
        await procPage.CreateProcessAsync(original);
        await procPage.OpenProcessDetailByNameAsync(BaseUrl, original);

        // The input is `required`, so whitespace (non-empty to the browser) is
        // submitted and rejected server-side with the es-CR required message.
        await procPage.RenameAsync("   ");

        await Expect(procPage.RenameError).ToContainTextAsync("El nombre es obligatorio.");
        await Expect(procPage.RenameInput).ToHaveValueAsync(original);
    }
}
