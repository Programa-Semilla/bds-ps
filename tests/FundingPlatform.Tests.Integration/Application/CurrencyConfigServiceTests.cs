using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 015 / US3 / T300 — covers <see cref="CurrencyConfigService"/>.
/// Asserts:
///  - Disable USD then re-enable round-trip persists state.
///  - Disable CRC throws (FR-002) — CRC is the platform's permanent base
///    currency and the entity's <c>Disable()</c> raises
///    <see cref="InvalidOperationException"/>.
///  - Audit-log entries are emitted via <see cref="ILogger"/> on every state
///    change using the
///    <see cref="MultiCurrencyAuditActions.CurrencyEnabled"/> /
///    <see cref="MultiCurrencyAuditActions.CurrencyDisabled"/> constants.
///
/// SCOPE LIMITATION: uses the EF InMemory provider for parity with the other
/// persistence-layer tests in this project (see
/// <c>ExchangeRateRepositoryTests</c>). The real SQL FK / RowVersion
/// behaviour is exercised end-to-end via the Aspire E2E suite (T302).
/// </summary>
[TestFixture]
public class CurrencyConfigServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static (CurrencyConfigService service, RecordingLogger<CurrencyConfigService> logger)
        BuildService(AppDbContext ctx)
    {
        ICurrencyRepository repo = new CurrencyRepository(ctx);
        var logger = new RecordingLogger<CurrencyConfigService>();
        return (new CurrencyConfigService(repo, logger), logger);
    }

    private static async Task SeedCatalogAsync(AppDbContext ctx)
    {
        ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
        ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar",          2, true, false, 2));
        await ctx.SaveChangesAsync();
    }

    [Test]
    public async Task Enable_ThenDisable_Usd_RoundTrips()
    {
        var dbName = $"currencies-roundtrip-{Guid.NewGuid():N}";
        using (var ctx = CreateContext(dbName))
        {
            await SeedCatalogAsync(ctx);
        }

        using (var ctx = CreateContext(dbName))
        {
            var (service, logger) = BuildService(ctx);

            // Round-trip: USD starts enabled per the seed.
            await service.DisableAsync(CurrencyCode.Usd, "actor-1");
            await service.EnableAsync(CurrencyCode.Usd, "actor-1");

            // Audit entries — one Disabled and one Enabled message.
            Assert.That(
                logger.Entries.Any(e => e.Message.Contains(MultiCurrencyAuditActions.CurrencyDisabled)),
                Is.True, "Disable should emit an audit entry.");
            Assert.That(
                logger.Entries.Any(e => e.Message.Contains(MultiCurrencyAuditActions.CurrencyEnabled)),
                Is.True, "Enable should emit an audit entry.");
        }

        using (var ctx = CreateContext(dbName))
        {
            var usd = await ctx.Currencies.FirstAsync(c => c.Code == CurrencyCode.Usd);
            Assert.That(usd.IsEnabled, Is.True);
        }
    }

    [Test]
    public async Task Disable_Crc_Throws_BaseCurrencyInvariant()
    {
        var dbName = $"currencies-base-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCatalogAsync(ctx);

        var (service, _) = BuildService(ctx);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DisableAsync(CurrencyCode.Crc, "actor-1"),
            "Disabling the base currency must throw.");

        var crc = await ctx.Currencies.FirstAsync(c => c.Code == CurrencyCode.Crc);
        Assert.That(crc.IsEnabled, Is.True, "CRC must remain enabled after a rejected disable attempt.");
    }

    [Test]
    public async Task ListAll_ListEnabled_ReturnExpectedCounts()
    {
        var dbName = $"currencies-list-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCatalogAsync(ctx);

        var (service, _) = BuildService(ctx);

        var all = await service.ListAllAsync();
        Assert.That(all, Has.Count.EqualTo(2));
        Assert.That(all.Select(c => c.Code.Value), Is.EquivalentTo(new[] { "CRC", "USD" }));

        await service.DisableAsync(CurrencyCode.Usd, "actor-1");
        var enabled = await service.ListEnabledAsync();
        Assert.That(enabled, Has.Count.EqualTo(1));
        Assert.That(enabled[0].Code, Is.EqualTo(CurrencyCode.Crc));
    }

    /// <summary>
    /// Captures <see cref="ILogger"/> entries so the test can assert that audit
    /// events are emitted on every state change.
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
