using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 015 — repository smoke tests for <see cref="ExchangeRateRepository"/>.
///
/// SCOPE LIMITATIONS (read first):
///   - These tests use the EF InMemory provider, matching the project's
///     existing integration-test convention (see SupplierRepositoryTests).
///     The InMemory provider does NOT enforce SQL Server unique indexes, so
///     the FR-007 duplicate-timestamp guard cannot be exercised end-to-end
///     here. That guard is enforced at three independent layers:
///     1. The dacpac-side UQ_ExchangeRates_PairAt unique index
///        (dbo.ExchangeRates.sql).
///     2. The repository's <c>DbUpdateException</c> (2627/2601) translation
///        to <see cref="DuplicateRateTimestampException"/>.
///     3. The E2E suite (AspireFixture) which boots a real SQL Server
///        container and exercises the admin "publish rate" form.
///   - The "latest by EffectiveAt" assertion below is portable to InMemory
///     and is the contract the application relies on.
/// </summary>
[TestFixture]
public class ExchangeRateRepositoryTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DateTime Past(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    [Test]
    public async Task GetLatestAsync_ReturnsMostRecentByEffectiveAt()
    {
        var dbName = $"rates-latest-{Guid.NewGuid():N}";
        var older = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 500m, 510m, Past(120), "u");
        var newer = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 525m, 530m, Past(5), "u");

        using (var ctx = CreateContext(dbName))
        {
            // Currencies catalog must exist for the FK; seed CRC + USD via the
            // domain ctor (RowVersion is ignored by InMemory).
            ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
            ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar",          2, true, false, 2));
            ctx.ExchangeRates.AddRange(older, newer);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ExchangeRateRepository(ctx);
            var latest = await repo.GetLatestAsync(CurrencyCode.Usd, CurrencyCode.Crc);

            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.Id, Is.EqualTo(newer.Id));
            Assert.That(latest.BuyRate, Is.EqualTo(525m));
        }
    }

    [Test]
    public async Task GetLatestAsync_NoRowsForPair_ReturnsNull()
    {
        var dbName = $"rates-empty-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var repo = new ExchangeRateRepository(ctx);
        var latest = await repo.GetLatestAsync(CurrencyCode.Usd, CurrencyCode.Crc);
        Assert.That(latest, Is.Null);
    }

    [Test]
    public async Task ListByPairAsync_OrdersDescendingByEffectiveAt()
    {
        var dbName = $"rates-list-{Guid.NewGuid():N}";
        var r1 = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 500m, 510m, Past(120), "u");
        var r2 = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 525m, 530m, Past(5),   "u");
        var r3 = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 510m, 515m, Past(60),  "u");

        using (var ctx = CreateContext(dbName))
        {
            ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
            ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar",          2, true, false, 2));
            ctx.ExchangeRates.AddRange(r1, r2, r3);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ExchangeRateRepository(ctx);
            var list = await repo.ListByPairAsync(CurrencyCode.Usd, CurrencyCode.Crc);

            Assert.That(list, Has.Count.EqualTo(3));
            Assert.That(list[0].Id, Is.EqualTo(r2.Id));
            Assert.That(list[1].Id, Is.EqualTo(r3.Id));
            Assert.That(list[2].Id, Is.EqualTo(r1.Id));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsMatchingRow()
    {
        var dbName = $"rates-byid-{Guid.NewGuid():N}";
        var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 525m, 530m, Past(5), "u");
        using (var ctx = CreateContext(dbName))
        {
            ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
            ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar",          2, true, false, 2));
            ctx.ExchangeRates.Add(rate);
            await ctx.SaveChangesAsync();
        }
        using (var ctx = CreateContext(dbName))
        {
            var repo = new ExchangeRateRepository(ctx);
            var fetched = await repo.GetByIdAsync(rate.Id);
            Assert.That(fetched, Is.Not.Null);
            Assert.That(fetched!.BuyRate, Is.EqualTo(525m));
        }
    }
}
