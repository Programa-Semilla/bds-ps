using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 015 / US1 — covers <see cref="Quotation"/> currency-aware behaviour:
///   - <see cref="Quotation.SetCurrencyAndAmountAsync"/> snapshots a USD rate and stamps converted CRC.
///   - <see cref="Quotation.SetCurrencyAndAmountAsync"/> short-circuits for CRC (no snapshot, ConvertedCrcAmount = price).
///   - <see cref="Quotation.EditAmount"/> re-applies the existing snapshot (no rate re-read).
///   - <see cref="Quotation.ChangeCurrencyAsync"/> clears the snapshot and re-applies a fresh one (FR-017a).
///   - <see cref="Quotation.AttachLegacyRate"/> clears <c>LegacyNeedsReview</c> and stamps converted CRC.
/// </summary>
[TestFixture]
public class QuotationCurrencyTests
{
    private static DateTime PastTimestamp => DateTime.UtcNow.AddMinutes(-10);

    private static Quotation NewQuotation(string currency = "CRC", decimal price = 1m) =>
        new(supplierId: 1, supplierBranchId: 1, documentId: 1, price: price,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), currency: currency);

    [Test]
    public async Task SetCurrencyAndAmountAsync_Usd_SnapshotsRateAndStampsConvertedCrc()
    {
        var quotation = NewQuotation(currency: "CRC", price: 0.01m);
        var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, PastTimestamp, "u");
        var conversion = new StubConversionService(rate);

        await quotation.SetCurrencyAndAmountAsync(CurrencyCode.Usd, 1000m, conversion);

        Assert.That(quotation.Currency, Is.EqualTo("USD"));
        Assert.That(quotation.Price, Is.EqualTo(1000m));
        Assert.That(quotation.ConvertedCrcAmount, Is.EqualTo(520_000.00m));
        Assert.That(quotation.Snapshot, Is.Not.Null);
        Assert.That(quotation.Snapshot!.RateRecordId, Is.EqualTo(rate.Id));
        Assert.That(quotation.Snapshot.RateValue, Is.EqualTo(520m));
        Assert.That(quotation.Snapshot.RateType, Is.EqualTo(RateType.Buy));
        Assert.That(quotation.LegacyNeedsReview, Is.False);
        Assert.That(rate.IsUsed, Is.True, "Source rate must be marked used per FR-008.");
    }

    [Test]
    public async Task SetCurrencyAndAmountAsync_Crc_ShortCircuits_NoSnapshotConvertedEqualsPrice()
    {
        var quotation = NewQuotation(currency: "CRC", price: 1m);
        var conversion = new StubConversionService();  // would throw if invoked

        await quotation.SetCurrencyAndAmountAsync(CurrencyCode.Crc, 750_000m, conversion);

        Assert.That(quotation.Currency, Is.EqualTo("CRC"));
        Assert.That(quotation.Price, Is.EqualTo(750_000m));
        Assert.That(quotation.ConvertedCrcAmount, Is.EqualTo(750_000m));
        Assert.That(quotation.Snapshot, Is.Null);
        Assert.That(quotation.LegacyNeedsReview, Is.False);
        Assert.That(conversion.CallCount, Is.EqualTo(0), "CRC must short-circuit without consulting the rate catalog.");
    }

    [Test]
    public async Task EditAmount_NonCrc_ReAppliesExistingSnapshot_NoRateLookup()
    {
        var quotation = NewQuotation();
        var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, PastTimestamp, "u");
        await quotation.SetCurrencyAndAmountAsync(CurrencyCode.Usd, 1000m, new StubConversionService(rate));

        // Author publishes a NEW (different) rate after the original save. EditAmount
        // must NOT pick this up — it re-uses the embedded snapshot per FR-016.
        quotation.EditAmount(2000m);

        Assert.That(quotation.Price, Is.EqualTo(2000m));
        Assert.That(quotation.ConvertedCrcAmount, Is.EqualTo(1_040_000.00m), "EditAmount must use the original snapshot's rate value.");
        Assert.That(quotation.Snapshot, Is.Not.Null);
        Assert.That(quotation.Snapshot!.RateValue, Is.EqualTo(520m));
    }

    [Test]
    public void EditAmount_LegacyNeedsReview_Throws()
    {
        var quotation = NewQuotation(currency: "USD", price: 1000m);
        // Force legacy state via the internal helper used by infra-level migration shims.
        typeof(Quotation).GetMethod("MarkLegacyNeedsReview",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(quotation, null);

        Assert.That(() => quotation.EditAmount(2000m), Throws.InvalidOperationException);
    }

    [Test]
    public async Task ChangeCurrencyAsync_ClearsExistingSnapshotAndReSnapshots()
    {
        var quotation = NewQuotation();
        var oldRate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 500m, 510m, PastTimestamp.AddDays(-1), "u");
        var newRate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 525m, 530m, PastTimestamp, "u");

        // Initial save against the old rate.
        await quotation.SetCurrencyAndAmountAsync(CurrencyCode.Usd, 100m, new StubConversionService(oldRate));
        Assert.That(quotation.Snapshot!.RateValue, Is.EqualTo(500m));

        // Switch to CRC then back to USD with a new rate available.
        await quotation.ChangeCurrencyAsync(CurrencyCode.Crc, new StubConversionService());
        Assert.That(quotation.Currency, Is.EqualTo("CRC"));
        Assert.That(quotation.Snapshot, Is.Null, "Snapshot must clear on switch to CRC.");

        await quotation.ChangeCurrencyAsync(CurrencyCode.Usd, new StubConversionService(newRate));
        Assert.That(quotation.Currency, Is.EqualTo("USD"));
        Assert.That(quotation.Snapshot, Is.Not.Null);
        Assert.That(quotation.Snapshot!.RateValue, Is.EqualTo(525m), "ChangeCurrencyAsync must re-read the latest rate (FR-017a).");
    }

    [Test]
    public void AttachLegacyRate_ClearsLegacyNeedsReviewAndStampsCrcAmount()
    {
        var quotation = NewQuotation(currency: "USD", price: 1000m);
        typeof(Quotation).GetMethod("MarkLegacyNeedsReview",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(quotation, null);
        Assert.That(quotation.LegacyNeedsReview, Is.True, "Pre-condition: legacy flag set.");

        var historicRate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 500m, 510m,
            DateTime.UtcNow.AddDays(-30), "admin");
        quotation.AttachLegacyRate(historicRate.ToSnapshot(RateType.Buy), convertedCrc: 500_000m);

        Assert.That(quotation.LegacyNeedsReview, Is.False);
        Assert.That(quotation.ConvertedCrcAmount, Is.EqualTo(500_000m));
        Assert.That(quotation.Snapshot, Is.Not.Null);
        Assert.That(quotation.Snapshot!.RateRecordId, Is.EqualTo(historicRate.Id));
    }

    /// <summary>
    /// Local stand-in for <see cref="IConversionService"/>. Returns the configured rate
    /// or throws when none is configured, so a "rate-less" CRC short-circuit test can
    /// detect accidental conversion calls.
    /// </summary>
    private sealed class StubConversionService : IConversionService
    {
        private readonly ExchangeRate? _rate;
        public int CallCount { get; private set; }

        public StubConversionService() { }
        public StubConversionService(ExchangeRate rate) { _rate = rate; }

        public Task<ConversionResult> ConvertAsync(
            CurrencyCode source, CurrencyCode target, decimal amount, CancellationToken ct = default)
        {
            CallCount++;
            if (_rate is null)
            {
                throw new InvalidOperationException("StubConversionService called with no configured rate.");
            }
            var converted = _rate.ConvertUsdToCrc(amount);
            return Task.FromResult(new ConversionResult(converted, _rate.ToSnapshot(RateType.Buy), _rate));
        }
    }
}
