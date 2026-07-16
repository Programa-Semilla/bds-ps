using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 045 — POM for the disbursement surface: the per-application index
/// (/Applications/{id}/Disbursements) with the balance card + record form + list, and the
/// per-disbursement detail (evidence upload, discrepancy panel, Validar/Cancel).
/// </summary>
public sealed class DisbursementPage : BasePage
{
    public DisbursementPage(IPage page) : base(page) { }

    // ---- Index ----
    public ILocator Surface => Page.Locator("[data-testid=disbursement-surface]");
    public ILocator Balance => Page.Locator("[data-testid=disbursement-balance]");
    public ILocator Rows => Page.Locator("[data-testid=disbursement-row]");
    public ILocator Empty => Page.Locator("[data-testid=disbursement-empty]");
    public ILocator RecordForm => Page.Locator("[data-testid=disbursement-record-form]");
    public ILocator OverDisbursedBanner => Page.Locator("[data-testid=balance-over-disbursed]");

    public async Task GotoAsync(string baseUrl, int applicationId)
        => await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/Disbursements");

    public async Task<int> GotoStatusAsync(string baseUrl, int applicationId)
    {
        var resp = await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/Disbursements");
        return resp?.Status ?? 0;
    }

    public async Task<string> BalanceText(string dimension)
        => (await Balance.Locator($"[data-testid=balance-{dimension}]").InnerTextAsync()).Trim();

    /// <summary>Fills + submits the record form on the Index page.</summary>
    public async Task RecordAsync(string isoDate, decimal amount, string bankTxn, string? bankAcct = null)
    {
        await Page.Locator("[data-testid=disbursement-payment-date]").FillAsync(isoDate);
        await Page.Locator("[data-testid=disbursement-amount]").FillAsync(Inv(amount));
        await Page.Locator("[data-testid=disbursement-bank-txn]").FillAsync(bankTxn);
        if (bankAcct is not null)
        {
            await Page.Locator("[data-testid=disbursement-bank-acct]").FillAsync(bankAcct);
        }
        await Page.Locator("[data-testid=disbursement-record-submit]").ClickAsync();
    }

    public ILocator RowById(int disbursementId)
        => Page.Locator($"[data-testid=disbursement-row][data-disbursement-id=\"{disbursementId}\"]");

    /// <summary>Opens the first disbursement row's detail page.</summary>
    public async Task OpenFirstAsync()
        => await Rows.First.Locator("[data-testid=disbursement-open]").ClickAsync();

    public async Task<int> FirstRowIdAsync()
        => int.Parse((await Rows.First.GetAttributeAsync("data-disbursement-id"))!);

    // ---- Detail ----
    public ILocator Detail => Page.Locator("[data-testid=disbursement-detail]");
    public ILocator DetailState => Page.Locator("[data-testid=disbursement-detail-state]");
    public ILocator Discrepancies => Page.Locator("[data-testid=disbursement-discrepancies]");
    public ILocator DiscrepancyItems => Page.Locator("[data-testid=disbursement-discrepancy]");
    public ILocator NoDiscrepancies => Page.Locator("[data-testid=disbursement-no-discrepancies]");
    public ILocator ValidateButton => Page.Locator("[data-testid=disbursement-validate]");
    public ILocator CancelButton => Page.Locator("[data-testid=disbursement-cancel]");
    public ILocator EditForm => Page.Locator("[data-testid=disbursement-edit-form]");
    public ILocator LockedNotice => Page.Locator("[data-testid=disbursement-locked-notice]");

    public ILocator EvidenceBlock(string kind) => Page.Locator($"[data-testid=disbursement-evidence-{kind}]");
    public ILocator EvidenceMissing(string kind) => Page.Locator($"[data-testid=disbursement-evidence-missing-{kind}]");
    public ILocator EvidenceDownload(string kind) => Page.Locator($"[data-testid=disbursement-evidence-download-{kind}]");

    public async Task GotoDetailAsync(string baseUrl, int applicationId, int disbursementId)
        => await Page.GotoAsync($"{baseUrl}/Applications/{applicationId}/Disbursements/{disbursementId}");

    /// <summary>Uploads/replaces one typed evidence document (kind = "BankReceipt" | "Invoice").</summary>
    public async Task AttachEvidenceAsync(string kind, decimal amount, string reference, string isoDate, string filePath)
    {
        await Page.Locator($"[data-testid=disbursement-evidence-amount-{kind}]").FillAsync(Inv(amount));
        await Page.Locator($"[data-testid=disbursement-evidence-ref-{kind}]").FillAsync(reference);
        await Page.Locator($"[data-testid=disbursement-evidence-date-{kind}]").FillAsync(isoDate);
        await Page.Locator($"[data-testid=disbursement-evidence-file-{kind}]").SetInputFilesAsync(filePath);
        await Page.Locator($"[data-testid=disbursement-evidence-submit-{kind}]").ClickAsync();
    }

    public async Task EditAmountAsync(decimal amount)
    {
        await EditForm.Locator("[data-testid=disbursement-edit-amount]").FillAsync(Inv(amount));
        await EditForm.Locator("[data-testid=disbursement-edit-submit]").ClickAsync();
    }

    public async Task ValidateAsync() => await ValidateButton.ClickAsync();

    public async Task CancelWithConfirmAsync()
    {
        await CancelButton.ClickAsync();
        await ConfirmInModalAsync();
    }

    private static string Inv(decimal v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
