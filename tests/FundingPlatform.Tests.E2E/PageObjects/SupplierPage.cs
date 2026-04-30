using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 013: rewritten as a step-flow POM. The Add page now has a single legal-ID
/// input that triggers a 250ms-debounced /Search call, and renders one of three
/// partials inline:
///   - _LookupHit (existing supplier + branch picker)
///   - _LookupEmpty (new-supplier form)
///   - _LookupRejected (contact-admin alert)
/// </summary>
public class SupplierPage : BasePage
{
    public SupplierPage(IPage page) : base(page) { }

    // Step 1 — legal ID input.
    public ILocator SupplierLegalIdInput => Page.GetByTestId("supplier-legal-id-input");

    // Step 2 — lookup result regions (server-rendered partials).
    public ILocator LookupResultRegion => Page.GetByTestId("lookup-result-region");
    public ILocator LookupHitCard => Page.GetByTestId("supplier-lookup-hit");
    public ILocator LookupEmptyCard => Page.GetByTestId("supplier-lookup-empty");
    public ILocator LookupRejectedAlert => Page.GetByTestId("supplier-lookup-rejected");
    public ILocator PendingVerificationBadge => Page.GetByTestId("pending-verification-badge");

    // Step 3a — branch picker (inside _LookupHit).
    public ILocator BranchPicker => Page.GetByTestId("branch-picker");
    public ILocator AddNewBranchToggle => Page.GetByTestId("add-new-branch-toggle");
    public ILocator NewBranchPanel => Page.GetByTestId("new-branch-panel");

    // Step 3b — new-branch form (inside the collapsible panel).
    public ILocator NewBranchNameInput => Page.GetByTestId("new-branch-name");
    public ILocator NewBranchContactInput => Page.GetByTestId("new-branch-contact");
    public ILocator NewBranchEmailInput => Page.GetByTestId("new-branch-email");
    public ILocator NewBranchPhoneInput => Page.GetByTestId("new-branch-phone");
    public ILocator NewBranchAddressInput => Page.GetByTestId("new-branch-address");
    public ILocator NewBranchProvinceInput => Page.GetByTestId("new-branch-province");

    // Step 3c — new-supplier form (inside _LookupEmpty).
    public ILocator NewSupplierNameInput => Page.GetByTestId("new-supplier-name-input");
    public ILocator NewSupplierBranchNameInput => Page.GetByTestId("new-supplier-branch-name");
    public ILocator NewSupplierBranchContactInput => Page.GetByTestId("new-supplier-branch-contact");
    public ILocator NewSupplierBranchEmailInput => Page.GetByTestId("new-supplier-branch-email");
    public ILocator NewSupplierBranchPhoneInput => Page.GetByTestId("new-supplier-branch-phone");
    public ILocator NewSupplierBranchProvinceInput => Page.GetByTestId("new-supplier-branch-province");

    // Quotation fields — always present.
    public ILocator PriceInput => Page.GetByTestId("quotation-price-input");
    public ILocator CurrencyInput => Page.GetByTestId("quotation-currency-input");
    public ILocator ValidUntilInput => Page.GetByTestId("quotation-validuntil-input");
    public ILocator QuotationFileInput => Page.GetByTestId("quotation-file-input");
    public ILocator SubmitButton => Page.GetByTestId("supplier-submit-button");
    public ILocator ValidationSummary => Page.GetByTestId("supplier-validation-summary");

    public async Task NavigateToAddAsync(int appId, int itemId, string baseUrl)
    {
        await Page.GotoAsync($"{baseUrl}/Application/{appId}/Item/{itemId}/Supplier/Add");
    }

    /// <summary>
    /// Type the legal ID and wait for the debounced lookup to render a partial
    /// in the lookup-result region. Returns the discriminator (Hit/Empty/Rejected)
    /// based on which partial card became visible.
    /// </summary>
    public async Task<string> SearchByLegalIdAsync(string legalId)
    {
        await SupplierLegalIdInput.FillAsync(legalId);
        // 250ms debounce + ~250ms server roundtrip. Wait up to 5s for any partial.
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"lookup-result-region\"]')?.children.length > 0",
            options: new() { Timeout = 5000 });

        if (await LookupHitCard.IsVisibleAsync()) return "Hit";
        if (await LookupRejectedAlert.IsVisibleAsync()) return "Rejected";
        if (await LookupEmptyCard.IsVisibleAsync()) return "Empty";
        return "Unknown";
    }

    public async Task SelectBranchAsync(int branchId)
    {
        await Page.GetByTestId($"branch-radio-{branchId}").CheckAsync();
    }

    public async Task SelectFirstBranchAsync()
    {
        var radios = Page.Locator("[data-testid^='branch-radio-']");
        await radios.First.CheckAsync();
    }

    public async Task FillQuotationFieldsAsync(decimal price, string validUntil, string filePath, string currency = "USD")
    {
        await PriceInput.FillAsync(price.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await CurrencyInput.FillAsync(currency);
        await ValidUntilInput.FillAsync(validUntil);
        await QuotationFileInput.SetInputFilesAsync(filePath);
    }

    public async Task FillNewSupplierFormAsync(
        string name, string branchName, string? contact = null, string? email = null,
        string? phone = null, string? province = null)
    {
        await NewSupplierNameInput.FillAsync(name);
        await NewSupplierBranchNameInput.FillAsync(branchName);
        if (contact is not null) await NewSupplierBranchContactInput.FillAsync(contact);
        if (email is not null) await NewSupplierBranchEmailInput.FillAsync(email);
        if (phone is not null) await NewSupplierBranchPhoneInput.FillAsync(phone);
        if (province is not null) await NewSupplierBranchProvinceInput.FillAsync(province);
    }

    public async Task OpenAddNewBranchPanelAsync()
    {
        await AddNewBranchToggle.ClickAsync();
    }

    public async Task FillNewBranchFormAsync(
        string branchName, string? contact = null, string? email = null,
        string? phone = null, string? address = null, string? province = null)
    {
        await NewBranchNameInput.FillAsync(branchName);
        if (contact is not null) await NewBranchContactInput.FillAsync(contact);
        if (email is not null) await NewBranchEmailInput.FillAsync(email);
        if (phone is not null) await NewBranchPhoneInput.FillAsync(phone);
        if (address is not null) await NewBranchAddressInput.FillAsync(address);
        if (province is not null) await NewBranchProvinceInput.FillAsync(province);
    }

    public async Task SubmitAsync()
    {
        await SubmitButton.ClickAsync();
    }

    /// <summary>
    /// Backwards-compat shim: spec 013 deleted the flat-supplier form. Older tests
    /// call this with the pre-spec-013 parameter set; we translate every call into
    /// the new search → new-supplier-form → quotation flow. Compliance flags are
    /// silently dropped (admin-only post-spec-013) — tests that need them set must
    /// call the admin Verify path explicitly.
    /// </summary>
    public async Task FillSupplierFormAsync(
        string legalId,
        string name,
        decimal price,
        string validUntil,
        string filePath,
        string? contactName = null,
        string? email = null,
        string? phone = null,
        string? location = null,
        bool isCompliantCCSS = false,
        bool isCompliantHacienda = false,
        bool isCompliantSICOP = false,
        string? currency = null)
    {
        var outcome = await SearchByLegalIdAsync(legalId);
        if (outcome == "Empty")
        {
            await FillNewSupplierFormAsync(
                name: name,
                branchName: "Sede principal",
                contact: contactName,
                email: email,
                phone: phone,
                province: location);
        }
        else if (outcome == "Hit")
        {
            // Re-using a supplier already in the catalog: pick the default branch.
            await SelectFirstBranchAsync();
        }
        // Rejected outcome: no save action. Caller will see the alert.

        await FillQuotationFieldsAsync(price, validUntil, filePath, currency ?? "USD");
    }
}
