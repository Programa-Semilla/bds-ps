using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 015 — converts an amount from a source currency to a target currency
/// using the latest reference rate. Lives in Domain (rather than Application)
/// because <see cref="Quotation.SetCurrencyAndAmount"/> takes it directly to
/// keep the rich-domain workflow self-contained without leaking Application
/// types upward into the entity.
///
/// Implementations are responsible for:
///  - returning the converted amount, the snapshot to embed on the Quotation,
///    and a reference to the source <see cref="ExchangeRate"/> so the caller
///    can mark it used after a successful save.
///  - throwing <see cref="MissingRateException"/> if no rate row exists for the
///    requested pair (FR-018).
/// </summary>
public interface IConversionService
{
    Task<ConversionResult> ConvertAsync(
        CurrencyCode source,
        CurrencyCode target,
        decimal amount,
        CancellationToken ct = default);
}

/// <summary>
/// Conversion outcome bundle. <see cref="Source"/> is the live entity; the
/// caller is expected to invoke <see cref="ExchangeRate.MarkUsed"/> after the
/// quotation persists so the rate becomes immutable per FR-008.
/// </summary>
public sealed record ConversionResult(
    decimal Converted,
    ExchangeRateSnapshot Snapshot,
    ExchangeRate Source);
