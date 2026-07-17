using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 047 — POM for the evidence-graph surface: the per-application index
/// (/Applications/{id}/Evidence) with the attach form + per-line allocation editor + completeness
/// matrix + evidence list, and the per-evidence detail (allocate/replace/version-history/download).
/// </summary>
public sealed class EvidencePage : BasePage
{
    public EvidencePage(IPage page) : base(page) { }

    public ILocator Surface => Page.Locator("[data-testid=evidence-graph]");
    public ILocator AttachForm => Page.Locator("[data-testid=evidence-attach-form]");
    public ILocator Rows => Page.Locator("[data-testid=evidence-row]");
    public ILocator Empty => Page.Locator("[data-testid=evidence-empty]");
    public ILocator CompletenessMatrix => Page.Locator("[data-testid=completeness-matrix]");
    public ILocator CompletenessRows => Page.Locator("[data-testid=completeness-row]");
    public ILocator AppContextHeader => Page.Locator("[data-testid=evidence-app-context]");
    public ILocator AppContextNumber => Page.Locator("[data-testid=evidence-app-number]");
    public ILocator AppContextApplicant => Page.Locator("[data-testid=evidence-app-applicant]");
    public ILocator BackToEvidence => Page.Locator("[data-testid=evidence-back]");

    public async Task GotoAsync(string baseUrl, int applicationId)
        => await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/EvidenceGraph");

    public async Task<int> GotoStatusAsync(string baseUrl, int applicationId)
    {
        var resp = await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/EvidenceGraph");
        return resp?.Status ?? 0;
    }

    /// <summary>Fills + submits the attach form. Allocation amounts are applied to the visible
    /// allocation-editor rows in order (null skips that row → orphan case when all null).</summary>
    public async Task AttachAsync(
        string type, decimal amount, string reference, string isoDate, string filePath,
        params decimal?[] lineAmounts)
    {
        await Page.Locator("[data-testid=evidence-type]").SelectOptionAsync(type);
        await Page.Locator("[data-testid=evidence-amount]").FillAsync(Inv(amount));
        await Page.Locator("[data-testid=evidence-reference]").FillAsync(reference);
        await Page.Locator("[data-testid=evidence-date]").FillAsync(isoDate);
        await Page.Locator("[data-testid=evidence-file]").SetInputFilesAsync(filePath);

        var allocInputs = AttachForm.Locator("[data-testid=alloc-amount]");
        var count = await allocInputs.CountAsync();
        for (var i = 0; i < count && i < lineAmounts.Length; i++)
        {
            if (lineAmounts[i] is { } amt)
            {
                await allocInputs.Nth(i).FillAsync(Inv(amt));
            }
        }
        await Page.Locator("[data-testid=evidence-attach-submit]").ClickAsync();
    }

    public async Task OpenFirstAsync()
        => await Rows.First.Locator("[data-testid=evidence-open]").ClickAsync();

    // ---- Detail ----
    public ILocator Detail => Page.Locator("[data-testid=evidence-detail]");
    public ILocator VersionRows => Page.Locator("[data-testid=evidence-version-row]");
    public ILocator ReplaceForm => Page.Locator("[data-testid=evidence-replace-form]");

    public async Task GotoDetailAsync(string baseUrl, int applicationId, int evidenceId)
        => await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/EvidenceGraph/{evidenceId}");

    public async Task ReplaceAsync(decimal amount, string reference, string isoDate, string reason, string? filePath = null)
    {
        await ReplaceForm.Locator("[data-testid=evidence-replace-amount]").FillAsync(Inv(amount));
        await ReplaceForm.Locator("[data-testid=evidence-replace-reference]").FillAsync(reference);
        await ReplaceForm.Locator("[name=documentDate]").FillAsync(isoDate);
        if (filePath is not null)
        {
            await ReplaceForm.Locator("[data-testid=evidence-replace-file]").SetInputFilesAsync(filePath);
        }
        await ReplaceForm.Locator("[data-testid=evidence-replace-reason]").FillAsync(reason);
        await ReplaceForm.Locator("[data-testid=evidence-replace-submit]").ClickAsync();
    }

    // ---- Completeness / closure (Index) ----
    public ILocator CompletenessRowFor(int itemId)
        => Page.Locator($"[data-testid=completeness-row][data-item-id=\"{itemId}\"]");

    public ILocator CloseButton => Page.Locator("[data-testid=line-close]").First;
    public ILocator ReopenButton => Page.Locator("[data-testid=line-reopen]").First;
    public ILocator IncompleteBadge => Page.Locator("[data-testid=completeness-incomplete-badge]").First;
    public ILocator ClosedBadge => Page.Locator("[data-testid=completeness-closed-badge]").First;

    private static string Inv(decimal v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
