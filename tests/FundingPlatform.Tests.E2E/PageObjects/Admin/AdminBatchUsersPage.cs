using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

/// <summary>
/// Spec 034 — drives the CSV bulk-upload page (<c>/Admin/Users/Batch</c>) and the
/// succeeded/errored result page.
/// </summary>
public class AdminBatchUsersPage : AdminBasePage
{
    public AdminBatchUsersPage(IPage page) : base(page)
    {
    }

    // Upload page
    public ILocator FileInput => Page.Locator("[data-testid=\"admin-users-batch-file\"]");
    public ILocator SubmitButton => Page.Locator("[data-testid=\"admin-users-batch-submit\"]");
    public ILocator TemplateLink => Page.Locator("[data-testid=\"admin-users-batch-template\"]");
    public ILocator FileError => Page.Locator("[data-testid=\"admin-users-batch-error\"]");

    // Result page
    public ILocator Summary => Page.Locator("[data-testid=\"admin-users-batch-summary\"]");
    public ILocator SucceededCount => Page.Locator("[data-testid=\"admin-users-batch-succeeded-count\"]");
    public ILocator ErroredCount => Page.Locator("[data-testid=\"admin-users-batch-errored-count\"]");
    public ILocator SucceededRows => Page.Locator("[data-testid=\"admin-users-batch-succeeded-row\"]");
    public ILocator ErroredRows => Page.Locator("[data-testid=\"admin-users-batch-errored-row\"]");

    public Task GoToAsync(string baseUrl) =>
        Page.GotoAsync($"{baseUrl}/Admin/Users/Batch");

    /// <summary>Sets the file input and submits the form.</summary>
    public async Task UploadAsync(string csvFilePath)
    {
        await FileInput.SetInputFilesAsync(csvFilePath);
        await SubmitButton.ScrollIntoViewIfNeededAsync();
        await SubmitButton.ClickAsync();
    }

    public async Task<int> SucceededCountValueAsync() =>
        int.Parse((await SucceededCount.InnerTextAsync()).Trim());

    public async Task<int> ErroredCountValueAsync() =>
        int.Parse((await ErroredCount.InnerTextAsync()).Trim());
}
