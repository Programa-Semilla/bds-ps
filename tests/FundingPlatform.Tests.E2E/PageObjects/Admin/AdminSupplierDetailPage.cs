using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

public class AdminSupplierDetailPage : AdminBasePage
{
    public AdminSupplierDetailPage(IPage page) : base(page) { }

    public ILocator EditForm => Page.GetByTestId("admin-supplier-edit-form");
    public ILocator NameInput => Page.GetByTestId("admin-supplier-name-input");
    public ILocator EInvoiceToggle => Page.GetByTestId("admin-supplier-einvoice-toggle");
    public ILocator CCSSToggle => Page.GetByTestId("admin-supplier-ccss-toggle");
    public ILocator HaciendaToggle => Page.GetByTestId("admin-supplier-hacienda-toggle");
    public ILocator SICOPToggle => Page.GetByTestId("admin-supplier-sicop-toggle");
    public ILocator EditSubmitButton => Page.GetByTestId("admin-supplier-edit-submit");

    public ILocator VerifyButton => Page.GetByTestId("admin-supplier-verify-button");
    public ILocator RejectForm => Page.GetByTestId("admin-supplier-reject-form");
    public ILocator RejectReasonInput => Page.GetByTestId("admin-supplier-reject-reason");
    public ILocator RejectButton => Page.GetByTestId("admin-supplier-reject-button");
    public ILocator RejectionReasonBanner => Page.GetByTestId("rejection-reason-banner");
    public ILocator SuccessBanner => Page.GetByTestId("success-banner");

    public ILocator BranchesTable => Page.GetByTestId("admin-supplier-branches-table");
    public ILocator BranchRow(int branchId) => Page.GetByTestId($"admin-branch-row-{branchId}");
    public ILocator BranchEditToggle(int branchId) => Page.GetByTestId($"admin-branch-edit-toggle-{branchId}");
    public ILocator BranchEditForm(int branchId) => Page.GetByTestId($"admin-branch-edit-form-{branchId}");
    public ILocator BranchEditSave(int branchId) => Page.GetByTestId($"admin-branch-save-{branchId}");

    public Task GoToAsync(string baseUrl, int supplierId) =>
        Page.GotoAsync($"{baseUrl}/Admin/Suppliers/{supplierId}");

    public async Task ToggleComplianceAllOnAsync()
    {
        // Use JS-set-checked because the form-check-input pattern doesn't auto-check on click in Playwright sometimes.
        if (!await EInvoiceToggle.IsCheckedAsync()) await EInvoiceToggle.CheckAsync();
        if (!await CCSSToggle.IsCheckedAsync()) await CCSSToggle.CheckAsync();
        if (!await HaciendaToggle.IsCheckedAsync()) await HaciendaToggle.CheckAsync();
        if (!await SICOPToggle.IsCheckedAsync()) await SICOPToggle.CheckAsync();
    }

    public async Task SaveEditAsync()
    {
        await EditSubmitButton.ClickAsync();
    }

    public async Task VerifyAsync()
    {
        // Spec 024 — verify now opens the shared confirm modal; click confirm to proceed.
        await VerifyButton.ClickAsync();
        await Page.Locator("#fl-shared-confirm-modal [data-testid=\"confirm-button\"]").ClickAsync();
    }

    public async Task RejectAsync(string reason)
    {
        await RejectReasonInput.FillAsync(reason);
        await RejectButton.ClickAsync();
    }
}
