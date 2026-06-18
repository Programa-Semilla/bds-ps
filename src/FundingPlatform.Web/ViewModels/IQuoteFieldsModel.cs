using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 023 / FR-003 — marker interface bound by the shared <c>_QuoteFields.cshtml</c>
/// partial. Implemented by both <see cref="AddSupplierViewModel"/> (Supplier/Add)
/// and <see cref="EditQuotationViewModel"/> (Quotation/Edit) so the partial can
/// resolve <c>asp-for="Price"</c>/<c>asp-for="Currency"</c>/<c>asp-for="ValidUntil"</c>
/// against the host model namespace transparently.
///
/// Spec 039 — extended with the required delivery-lead-time and warranty fields
/// (value + unit) so the same partial renders them on both the add and edit paths.
/// </summary>
public interface IQuoteFieldsModel
{
    decimal Price { get; set; }
    string Currency { get; set; }
    DateOnly ValidUntil { get; set; }
    IReadOnlyList<CurrencyOption> EnabledCurrencies { get; set; }

    int DeliveryLeadTimeValue { get; set; }
    DurationUnit DeliveryLeadTimeUnit { get; set; }
    int WarrantyValue { get; set; }
    DurationUnit WarrantyUnit { get; set; }
}
