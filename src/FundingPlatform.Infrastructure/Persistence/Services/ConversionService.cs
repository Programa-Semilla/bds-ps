using FundingPlatform.Application.Errors;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Infrastructure.Persistence.Services;

/// <summary>
/// Spec 015 — default <see cref="IConversionService"/>. Looks up the latest
/// rate for the requested pair, applies <see cref="ExchangeRate.ConvertUsdToCrc"/>
/// for the USD→CRC direction, and returns a snapshot for embedding on the
/// caller's <see cref="Quotation"/>.
///
/// Throws <see cref="MissingRateException"/> if the catalog has no rate for the
/// pair (FR-018). The caller is responsible for invoking
/// <see cref="ExchangeRate.MarkUsed"/> after their save commits — this service
/// does NOT mutate state on the rate row, so a failed save will not orphan a
/// MarkUsed update.
/// </summary>
public class ConversionService : IConversionService
{
    private readonly IExchangeRateRepository _rates;

    public ConversionService(IExchangeRateRepository rates)
    {
        _rates = rates;
    }

    public async Task<ConversionResult> ConvertAsync(
        CurrencyCode source,
        CurrencyCode target,
        decimal amount,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var rate = await _rates.GetLatestAsync(source, target, ct).ConfigureAwait(false)
            ?? throw new MissingRateException(source, target);

        // MVP applies Buy direction only (data-model.md). Sell is captured on
        // the rate row for audit but not used here.
        var converted = rate.ConvertUsdToCrc(amount);
        var snapshot = rate.ToSnapshot(RateType.Buy);
        return new ConversionResult(converted, snapshot, rate);
    }
}
