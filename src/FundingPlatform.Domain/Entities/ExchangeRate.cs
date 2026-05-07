using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 015 — administrator-published reference rate between a source and target
/// currency. Immutable once it has been snapshotted by a Quotation (<see cref="IsUsed"/>);
/// admins must publish a superseding row instead of editing or deleting (FR-008).
///
/// MVP applies <see cref="BuyRate"/> when converting USD → CRC for storage;
/// <see cref="SellRate"/> is captured for audit only.
/// </summary>
public class ExchangeRate
{
    public Guid Id { get; private set; }
    public CurrencyCode SourceCurrency { get; private set; } = null!;
    public CurrencyCode TargetCurrency { get; private set; } = null!;
    public decimal BuyRate { get; private set; }
    public decimal SellRate { get; private set; }
    public DateTime EffectiveAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public bool IsUsed { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ExchangeRate() { }

    public ExchangeRate(
        CurrencyCode sourceCurrency,
        CurrencyCode targetCurrency,
        decimal buyRate,
        decimal sellRate,
        DateTime effectiveAtUtc,
        string createdByUserId)
    {
        ArgumentNullException.ThrowIfNull(sourceCurrency);
        ArgumentNullException.ThrowIfNull(targetCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);

        if (buyRate <= 0m)
        {
            throw new ArgumentException("Buy rate must be greater than zero.", nameof(buyRate));
        }
        if (sellRate <= 0m)
        {
            throw new ArgumentException("Sell rate must be greater than zero.", nameof(sellRate));
        }
        if (sourceCurrency == targetCurrency)
        {
            throw new ArgumentException("Source and target currencies must differ.", nameof(targetCurrency));
        }
        if (effectiveAtUtc > DateTime.UtcNow)
        {
            throw new ArgumentException("Effective timestamp cannot be in the future.", nameof(effectiveAtUtc));
        }

        Id = Guid.NewGuid();
        SourceCurrency = sourceCurrency;
        TargetCurrency = targetCurrency;
        BuyRate = buyRate;
        SellRate = sellRate;
        EffectiveAtUtc = effectiveAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTime.UtcNow;
        IsUsed = false;
    }

    /// <summary>
    /// Converts a USD amount to CRC using <see cref="BuyRate"/>. Half-away-from-zero
    /// rounding to two decimal places per spec clarification (decimal-only path).
    /// </summary>
    public decimal ConvertUsdToCrc(decimal usdAmount)
    {
        var raw = usdAmount * BuyRate;
        return Math.Round(raw, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Builds a snapshot of this rate for embedding on a Quotation. The snapshot
    /// is immutable and decouples the quote from later edits to the source row.
    /// </summary>
    public ExchangeRateSnapshot ToSnapshot(RateType type)
    {
        var rateValue = type switch
        {
            RateType.Buy => BuyRate,
            RateType.Sell => SellRate,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown rate type."),
        };
        return new ExchangeRateSnapshot(Id, rateValue, type, EffectiveAtUtc);
    }

    /// <summary>One-way idempotent transition from unused to used. Repeated calls are no-ops.</summary>
    public void MarkUsed()
    {
        if (!IsUsed)
        {
            IsUsed = true;
        }
    }
}
