using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

public class SigningStagePanelPage : BasePage
{
    public SigningStagePanelPage(IPage page) : base(page)
    {
    }

    public ILocator Panel => Page.Locator("#funding-agreement-panel");
    public ILocator ExecutedBadge => Page.Locator("[data-testid=funding-agreement-executed-badge]");
    public ILocator PendingCard => Page.Locator("[data-testid=signed-upload-pending]");
    public ILocator UploadInput => Page.Locator("[data-testid=signed-upload-file]");
    public ILocator UploadSubmitButton => Page.Locator("[data-testid=signed-upload-submit]");
    public ILocator ReplaceInput => Page.Locator("[data-testid=signed-upload-replace-file]");
    public ILocator ReplaceSubmitButton => Page.Locator("[data-testid=signed-upload-replace]");
    public ILocator WithdrawButton => Page.Locator("[data-testid=signed-upload-withdraw]");
    public ILocator ApproveButton => Page.Locator("[data-testid=signed-upload-approve]");
    public ILocator ApproveCommentInput => Page.Locator("[data-testid=signed-upload-approve-comment]");
    public ILocator RejectButton => Page.Locator("[data-testid=signed-upload-reject]");
    public ILocator RejectCommentInput => Page.Locator("[data-testid=signed-upload-reject-comment]");
    public ILocator LastRejectionNotice => Page.Locator("[data-testid=signed-upload-last-rejection]");
    public ILocator SignedDownloadLink => Page.Locator("[data-testid=signed-agreement-download]");
    public ILocator VersionMismatchHint => Page.Locator("[data-testid=signed-upload-version-mismatch]");

    public async Task UploadSigned(string filePath)
    {
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await UploadInput.SetInputFilesAsync(filePath);
        await UploadSubmitButton.ClickAsync();
    }

    public async Task<bool> IsPendingUploadVisible()
    {
        return await PendingCard.CountAsync() > 0;
    }

    // Spec 027 / US2 — Aprobar/Rechazar now route through the shared confirm
    // modal (data-confirm). Click the action, then confirm to commit.
    public ILocator ConfirmModalButton => Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]");
    public ILocator ConfirmModalCancelButton => Page.Locator("#fl-shared-confirm-modal [data-testid=\"cancel-button\"]");
    public ILocator ConfirmModalBody => Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-rationale\"]");

    public async Task ApprovePending(string? comment = null)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            await ApproveCommentInput.FillAsync(comment);
        }
        await ApproveButton.ClickAsync();
        await ConfirmModalButton.ClickAsync();
    }

    public async Task RejectPending(string comment)
    {
        await RejectCommentInput.FillAsync(comment);
        await RejectButton.ClickAsync();
        await ConfirmModalButton.ClickAsync();
    }

    public async Task ReplacePending(string filePath)
    {
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await ReplaceInput.SetInputFilesAsync(filePath);
        await ReplaceSubmitButton.ClickAsync();
    }

    public async Task WithdrawPending()
    {
        // Spec 024 — withdraw now opens the shared confirm modal; click confirm to proceed.
        await WithdrawButton.ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
    }

    public async Task<bool> IsExecutedBadgeVisible()
    {
        return await ExecutedBadge.CountAsync() > 0;
    }

    public async Task<bool> IsRejectionCommentVisible(string expectedComment)
    {
        if (await LastRejectionNotice.CountAsync() == 0) return false;
        var text = await LastRejectionNotice.TextContentAsync();
        return text is not null && text.Contains(expectedComment, StringComparison.OrdinalIgnoreCase);
    }

    public ILocator SignedDownload => SignedDownloadLink;
}
