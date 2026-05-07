using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class ExchangeRateTests
{
    private static DateTime PastTimestamp => DateTime.UtcNow.AddMinutes(-5);

    [Test]
    public void Constructor_AcceptsValidInputs()
    {
        var rate = new ExchangeRate(
            CurrencyCode.Usd, CurrencyCode.Crc,
            buyRate: 520.5m, sellRate: 525.0m,
            effectiveAtUtc: PastTimestamp,
            createdByUserId: "user-1");

        Assert.That(rate.SourceCurrency, Is.EqualTo(CurrencyCode.Usd));
        Assert.That(rate.TargetCurrency, Is.EqualTo(CurrencyCode.Crc));
        Assert.That(rate.BuyRate, Is.EqualTo(520.5m));
        Assert.That(rate.SellRate, Is.EqualTo(525.0m));
        Assert.That(rate.IsUsed, Is.False);
        Assert.That(rate.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void Constructor_RejectsNonPositiveBuyRate(
        [Values(0, -0.01)] double invalid)
    {
        Assert.Throws<ArgumentException>(() => new ExchangeRate(
            CurrencyCode.Usd, CurrencyCode.Crc,
            buyRate: (decimal)invalid, sellRate: 1m,
            effectiveAtUtc: PastTimestamp, createdByUserId: "u"));
    }

    [Test]
    public void Constructor_RejectsNonPositiveSellRate()
    {
        Assert.Throws<ArgumentException>(() => new ExchangeRate(
            CurrencyCode.Usd, CurrencyCode.Crc,
            buyRate: 1m, sellRate: 0m,
            effectiveAtUtc: PastTimestamp, createdByUserId: "u"));
    }

    [Test]
    public void Constructor_RejectsSameSourceAndTarget()
    {
        Assert.Throws<ArgumentException>(() => new ExchangeRate(
            CurrencyCode.Usd, CurrencyCode.Usd,
            buyRate: 1m, sellRate: 1m,
            effectiveAtUtc: PastTimestamp, createdByUserId: "u"));
    }

    [Test]
    public void Constructor_RejectsFutureEffectiveTimestamp()
    {
        Assert.Throws<ArgumentException>(() => new ExchangeRate(
            CurrencyCode.Usd, CurrencyCode.Crc,
            buyRate: 1m, sellRate: 1m,
            effectiveAtUtc: DateTime.UtcNow.AddHours(1),
            createdByUserId: "u"));
    }

    [Test]
    public void MarkUsed_IsIdempotent()
    {
        var rate = NewRate();
        rate.MarkUsed();
        rate.MarkUsed();
        Assert.That(rate.IsUsed, Is.True);
    }

    [Test]
    public void ConvertUsdToCrc_RoundsHalfAwayFromZero_05Up()
    {
        // 1.005 * 100 = 100.5, rounds to 100.50 (away from zero rounds to .51 only at midpoint
        // of last cent). Pin a clearer midpoint: 0.005 amount * rate of 1.0 => 0.005 → rounds to 0.01.
        var rate = NewRate(buy: 1m);
        var result = rate.ConvertUsdToCrc(0.005m);
        Assert.That(result, Is.EqualTo(0.01m));
    }

    [Test]
    public void ConvertUsdToCrc_RoundsHalfAwayFromZero_04Down()
    {
        var rate = NewRate(buy: 1m);
        var result = rate.ConvertUsdToCrc(0.004m);
        Assert.That(result, Is.EqualTo(0.00m));
    }

    [Test]
    public void ConvertUsdToCrc_AppliesBuyRate()
    {
        var rate = NewRate(buy: 520m);
        var result = rate.ConvertUsdToCrc(1000m);
        Assert.That(result, Is.EqualTo(520_000.00m));
    }

    [Test]
    public void ConvertUsdToCrc_RoundsRawProductAtTwoDecimals()
    {
        // 1.235 * 100 = 123.5 → 123.50; 1.236 * 100 = 123.6 → 123.60.
        // 1.005 * 100 = 100.5 → 100.50.
        var rate = NewRate(buy: 100m);
        Assert.That(rate.ConvertUsdToCrc(1.005m), Is.EqualTo(100.50m));
    }

    [Test]
    public void ToSnapshot_BuyType_UsesBuyRate()
    {
        var rate = NewRate(buy: 520.123456m, sell: 525m);
        var snap = rate.ToSnapshot(RateType.Buy);
        Assert.That(snap.RateRecordId, Is.EqualTo(rate.Id));
        Assert.That(snap.RateValue, Is.EqualTo(520.123456m));
        Assert.That(snap.RateType, Is.EqualTo(RateType.Buy));
        Assert.That(snap.EffectiveAtUtc, Is.EqualTo(rate.EffectiveAtUtc));
    }

    [Test]
    public void ToSnapshot_SellType_UsesSellRate()
    {
        var rate = NewRate(buy: 520m, sell: 525m);
        var snap = rate.ToSnapshot(RateType.Sell);
        Assert.That(snap.RateValue, Is.EqualTo(525m));
        Assert.That(snap.RateType, Is.EqualTo(RateType.Sell));
    }

    private static ExchangeRate NewRate(decimal buy = 520m, decimal sell = 525m) =>
        new(CurrencyCode.Usd, CurrencyCode.Crc, buy, sell, PastTimestamp, "user-1");
}
