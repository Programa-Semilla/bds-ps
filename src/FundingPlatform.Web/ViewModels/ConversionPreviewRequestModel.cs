namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 015 / contract <c>conversion-preview-api.md</c> — JSON body for
/// <c>POST /Application/{appId}/Item/{itemId}/Quotation/Convert</c>.
///
/// Bound via <c>[FromBody]</c>; client-side <c>quote-conversion-preview.js</c>
/// posts <c>{ "currencyCode": "USD", "amount": 1000.00 }</c> and the server
/// computes the conversion server-side (FR-019: client never multiplies).
/// </summary>
public sealed class ConversionPreviewRequestModel
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
