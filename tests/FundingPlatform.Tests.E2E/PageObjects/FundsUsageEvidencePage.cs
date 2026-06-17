using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>Spec 036 — POM for the funds-usage evidence stage (/Applications/{id}/Evidence).</summary>
public sealed class FundsUsageEvidencePage : BasePage
{
    public FundsUsageEvidencePage(IPage page) : base(page) { }

    public ILocator Stage => Page.Locator("[data-testid=evidence-stage]");
    public ILocator FileInput => Page.Locator("[data-testid=evidence-file-input]");
    public ILocator UploadNoteInput => Page.Locator("[data-testid=evidence-note-input]");
    public ILocator UploadSubmit => Page.Locator("[data-testid=evidence-upload-submit]");
    public ILocator Rows => Page.Locator("[data-testid=evidence-row]");
    public ILocator Empty => Page.Locator("[data-testid=evidence-empty]");

    public ILocator RowFor(string fileName) =>
        Rows.Filter(new LocatorFilterOptions { HasText = fileName });

    public async Task GotoAsync(string baseUrl, int applicationId)
        => await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/Evidence");

    public async Task<int> GotoStatusAsync(string baseUrl, int applicationId)
    {
        var resp = await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/Evidence");
        return resp?.Status ?? 0;
    }

    public async Task UploadAsync(string filePath, string? note = null)
    {
        await FileInput.SetInputFilesAsync(filePath);
        if (note is not null)
        {
            await UploadNoteInput.FillAsync(note);
        }
        await UploadSubmit.ClickAsync();
    }

    /// <summary>The first row's download URL (relative), for cross-role refusal checks.</summary>
    public async Task<string> FirstDownloadHrefAsync()
        => await Rows.First.Locator("[data-testid=evidence-download]").GetAttributeAsync("href") ?? string.Empty;

    public async Task DownloadRowAsync(string fileName)
    {
        var row = RowFor(fileName);
        await Page.RunAndWaitForDownloadAsync(async () =>
        {
            await row.Locator("[data-testid=evidence-download]").ClickAsync();
        });
    }

    public async Task SaveNoteAsync(string fileName, string note)
    {
        var row = RowFor(fileName);
        await row.Locator("[data-testid=evidence-note-edit]").FillAsync(note);
        await row.Locator("[data-testid=evidence-note-save]").ClickAsync();
    }

    /// <summary>Sets a note value bypassing the maxlength attribute (to exercise the
    /// server-side &gt;250 guard) and submits the row's note form.</summary>
    public async Task SaveOversizeNoteAsync(string fileName, string note)
    {
        var row = RowFor(fileName);
        var textarea = row.Locator("[data-testid=evidence-note-edit]");
        await textarea.EvaluateAsync("(el, v) => { el.removeAttribute('maxlength'); el.value = v; }", note);
        await row.Locator("[data-testid=evidence-note-save]").ClickAsync();
    }

    public async Task DeleteWithConfirmAsync(string fileName)
    {
        await RowFor(fileName).Locator("[data-testid=evidence-delete]").ClickAsync();
        await ConfirmInModalAsync();
    }

    public async Task DeleteThenCancelAsync(string fileName)
    {
        await RowFor(fileName).Locator("[data-testid=evidence-delete]").ClickAsync();
        await SharedConfirmCancel.ClickAsync();
    }
}
