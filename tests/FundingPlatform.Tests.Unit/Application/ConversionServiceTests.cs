using FundingPlatform.Application.Errors;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence.Services;

namespace FundingPlatform.Tests.Unit.Application;

[TestFixture]
public class ConversionServiceTests
{
    private static DateTime Past(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    [Test]
    public void ConvertAsync_NoRateConfigured_Throws()
    {
        var repo = new FakeExchangeRateRepository();
        var sut = new ConversionService(repo);

        Assert.That(async () =>
            await sut.ConvertAsync(CurrencyCode.Usd, CurrencyCode.Crc, 1000m),
            Throws.TypeOf<MissingRateException>());
    }

    [Test]
    public async Task ConvertAsync_PicksLatestByEffectiveAt()
    {
        var older = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc,
            500m, 510m, Past(120), "u");
        var newer = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc,
            520m, 530m, Past(5), "u");

        var repo = new FakeExchangeRateRepository(newer, older);
        var sut = new ConversionService(repo);

        var result = await sut.ConvertAsync(CurrencyCode.Usd, CurrencyCode.Crc, 1000m);

        Assert.That(result.Source.Id, Is.EqualTo(newer.Id));
        Assert.That(result.Converted, Is.EqualTo(520_000.00m));
        Assert.That(result.Snapshot.RateValue, Is.EqualTo(520m));
        Assert.That(result.Snapshot.RateRecordId, Is.EqualTo(newer.Id));
    }

    [Test]
    public async Task ConvertAsync_ReturnsLiveSourceForMarkUsedByCaller()
    {
        var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc,
            520m, 525m, Past(1), "u");
        var sut = new ConversionService(new FakeExchangeRateRepository(rate));

        var result = await sut.ConvertAsync(CurrencyCode.Usd, CurrencyCode.Crc, 100m);

        Assert.That(result.Source.IsUsed, Is.False, "ConversionService must not mutate the rate; the caller decides.");
        result.Source.MarkUsed();
        Assert.That(result.Source.IsUsed, Is.True);
    }

    private sealed class FakeExchangeRateRepository : IExchangeRateRepository
    {
        private readonly List<ExchangeRate> _rates;

        public FakeExchangeRateRepository(params ExchangeRate[] rates)
        {
            _rates = rates.ToList();
        }

        public Task<ExchangeRate?> GetLatestAsync(CurrencyCode source, CurrencyCode target, CancellationToken ct = default)
            => Task.FromResult(_rates
                .Where(r => r.SourceCurrency == source && r.TargetCurrency == target)
                .OrderByDescending(r => r.EffectiveAtUtc)
                .FirstOrDefault());

        public Task<IReadOnlyList<ExchangeRate>> ListByPairAsync(CurrencyCode source, CurrencyCode target, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExchangeRate>>(_rates
                .Where(r => r.SourceCurrency == source && r.TargetCurrency == target)
                .OrderByDescending(r => r.EffectiveAtUtc).ToList());

        public Task AddAsync(ExchangeRate rate, CancellationToken ct = default)
        {
            _rates.Add(rate);
            return Task.CompletedTask;
        }

        public Task<ExchangeRate?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_rates.FirstOrDefault(r => r.Id == id));
    }
}
