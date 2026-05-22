namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 023 / FR-003 — marker interface bound by the shared <c>_QuoteFields.cshtml</c>
/// partial. Implemented by both <see cref="AddSupplierViewModel"/> (Supplier/Add)
/// and <see cref="EditQuotationViewModel"/> (Quotation/Edit) so the partial can
/// resolve <c>asp-for="Price"</c>/<c>asp-for="Currency"</c>/<c>asp-for="ValidUntil"</c>
/// against the host model namespace transparently.
/// </summary>
public interface IQuoteFieldsModel
{
    decimal Price { get; set; }
    string Currency { get; set; }
    DateOnly ValidUntil { get; set; }
    IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; }
}
