using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 044 / US1 (T024) — admin reception-window CRUD on the Process detail card.
/// Creates two non-contiguous windows, edits one, deactivates one, deletes one, and
/// rejects an end≤start window; asserts the per-row state badges. No applicant flow.
/// </summary>
[Category("ReceptionWindow")]
public class ReceptionWindowAdminTests : ReceptionWindowE2EBase
{
    // datetime-local values are interpreted as Costa Rica local (UTC−6) by the server.
    private static string CrLocal(TimeSpan offsetFromNow)
        => DateTime.UtcNow.AddHours(-6).Add(offsetFromNow).ToString("yyyy-MM-ddTHH:mm");

    private ILocator Card => Page.Locator("[data-testid=admin-process-reception-windows-card]");
    private ILocator Rows => Page.Locator("[data-testid=reception-window-row]");
    private ILocator Empty => Page.Locator("[data-testid=reception-window-empty]");

    private async Task CreateWindowAsync(string name, string startLocal, string endLocal)
    {
        await Page.FillAsync("[data-testid=reception-window-name]", name);
        await Page.FillAsync("[data-testid=reception-window-start]", startLocal);
        await Page.FillAsync("[data-testid=reception-window-end]", endLocal);
        await Page.Locator("[data-testid=reception-window-create-submit]").ClickAsync();
    }

    private ILocator RowByName(string name) =>
        Rows.Filter(new() { Has = Page.Locator("[data-testid=reception-window-row-name]", new() { HasText = name }) });

    [Test]
    public async Task AdminCanFullyManageReceptionWindows()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await RegisterAdminAndLoginAsync(unique);
        var processId = await AdminCreateProcessWithGroupAsync($"RWProc-{unique}", $"RWG-{unique}");

        await Page.GotoAsync($"{BaseUrl}/Admin/Processes/{processId}");
        await Expect(Card).ToBeVisibleAsync();
        await Expect(Empty).ToBeVisibleAsync();

        // Two non-contiguous windows: one open now, one upcoming.
        await CreateWindowAsync($"Abierta-{unique}", CrLocal(TimeSpan.FromDays(-1)), CrLocal(TimeSpan.FromDays(2)));
        await Expect(RowByName($"Abierta-{unique}")).ToBeVisibleAsync();

        await CreateWindowAsync($"Proxima-{unique}", CrLocal(TimeSpan.FromDays(10)), CrLocal(TimeSpan.FromDays(12)));
        await Expect(RowByName($"Proxima-{unique}")).ToBeVisibleAsync();
        await Expect(Rows).ToHaveCountAsync(2);

        // State badges reflect the instants.
        await Expect(RowByName($"Abierta-{unique}").Locator("[data-testid=reception-window-state]"))
            .ToHaveTextAsync(new Regex("Abierta"));
        await Expect(RowByName($"Proxima-{unique}").Locator("[data-testid=reception-window-state]"))
            .ToHaveTextAsync(new Regex("Próxima"));

        // Edit the upcoming window's name.
        var proxRow = RowByName($"Proxima-{unique}");
        await proxRow.Locator("[data-testid=reception-window-edit-toggle]").ClickAsync();
        await proxRow.Locator("[data-testid=reception-window-edit-name]").FillAsync($"Renombrada-{unique}");
        await proxRow.Locator("[data-testid=reception-window-edit-submit]").ClickAsync();
        await Expect(RowByName($"Renombrada-{unique}")).ToBeVisibleAsync();

        // Deactivate the open window → badge becomes Inactiva.
        await RowByName($"Abierta-{unique}").Locator("[data-testid=reception-window-toggle-active]").ClickAsync();
        await Expect(RowByName($"Abierta-{unique}").Locator("[data-testid=reception-window-state]"))
            .ToHaveTextAsync(new Regex("Inactiva"));

        // Delete the renamed window (spec-024 shared confirm modal).
        await RowByName($"Renombrada-{unique}").Locator("[data-testid=reception-window-delete]").ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
        await Expect(RowByName($"Renombrada-{unique}")).ToHaveCountAsync(0);
        await Expect(Rows).ToHaveCountAsync(1);
    }

    [Test]
    public async Task EndBeforeStart_IsRejected_WithEsCrMessage()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        await RegisterAdminAndLoginAsync(unique);
        var processId = await AdminCreateProcessWithGroupAsync($"RWBad-{unique}", $"RWGB-{unique}");

        await Page.GotoAsync($"{BaseUrl}/Admin/Processes/{processId}");
        // end before start.
        await CreateWindowAsync($"Mala-{unique}", CrLocal(TimeSpan.FromDays(5)), CrLocal(TimeSpan.FromDays(2)));

        // No row created; an es-CR validation message surfaces as an error toast/banner.
        await Expect(Page.Locator("[data-testid=error-banner]")).ToContainTextAsync("La fecha de cierre debe ser posterior");
        await Expect(Rows).ToHaveCountAsync(0);
    }
}
