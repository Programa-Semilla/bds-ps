namespace FundingPlatform.Application.DTOs;

/// <summary>
/// Spec 015 / contract <c>conversion-preview-api.md</c> — JSON shape returned
/// by <c>POST /Application/{appId}/Item/{itemId}/Quotation/Convert</c>.
///
/// CRC short-circuit: when the user picks CRC, the controller returns
/// <c>{ isCrc: true, amount: <input> }</c> without consulting any rate; both
/// <see cref="OriginalCurrencyCode"/> and <see cref="Rate"/> are null in that case.
/// </summary>
public sealed record ConversionPreviewDto(
    bool IsCrc,
    decimal Amount,
    string? OriginalCurrencyCode,
    decimal? OriginalAmount,
    decimal? ConvertedCrcAmount,
    ConversionPreviewRateDto? Rate);

/// <summary>
/// Embedded snapshot of the rate that produced the preview. Mirrors
/// <see cref="FundingPlatform.Domain.ValueObjects.ExchangeRateSnapshot"/>.
/// </summary>
public sealed record ConversionPreviewRateDto(
    Guid RateRecordId,
    decimal RateValue,
    string RateType,
    DateTime EffectiveAtUtc);
