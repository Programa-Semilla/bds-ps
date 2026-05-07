using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 015 / US3 / T301 — covers <see cref="ExchangeRateService"/>. Asserts:
///   - Valid input creates a row + emits the
///     <see cref="MultiCurrencyAuditActions.ExchangeRateCreated"/> audit entry.
///   - Zero/negative buy or sell rejected with
///     <see cref="UserFacingErrorCode.OperationRejected"/>.
///   - Future-dated effective timestamp rejected with
///     <see cref="UserFacingErrorCode.FutureDatedRateRejected"/> (FR-007a).
///   - Duplicate-timestamp surfaces from the repository as
///     <see cref="UserFacingErrorCode.DuplicateRateTimestamp"/> (FR-007).
///   - Edit and delete attempts routed through the service emit the
///     blocked-attempt audit entries (FR-008 / FR-010).
///
/// SCOPE: uses the EF InMemory provider (CurrencyRepository convention) for
/// the success path; the duplicate-timestamp scenario uses a tiny in-process
/// stub that throws <see cref="DuplicateRateTimestampException"/>, since the
/// InMemory provider cannot enforce SQL Server unique indexes (see
/// <c>ExchangeRateRepositoryTests</c> for the rationale). The real SQL
/// constraint is exercised end-to-end via the Aspire E2E suite (T303).
/// </summary>
[TestFixture]
public class ExchangeRateServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DateTime Past(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    private static async Task SeedCurrenciesAsync(AppDbContext ctx)
    {
        ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
        ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar",          2, true, false, 2));
        await ctx.SaveChangesAsync();
    }

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

    [Test]
    public async Task CreateAsync_ValidInput_PersistsAndEmitsAuditEntry()
    {
        var dbName = $"rates-create-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCurrenciesAsync(ctx);

        var repo = new ExchangeRateRepository(ctx);
        var logger = new RecordingLogger<ExchangeRateService>();
        var service = new ExchangeRateService(repo, logger);

        var rate = await service.CreateAsync(
            CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, Past(5), "actor-1");

        Assert.That(rate.BuyRate, Is.EqualTo(520m));
        Assert.That(rate.SellRate, Is.EqualTo(525m));

        var persisted = await ctx.ExchangeRates.SingleAsync();
        Assert.That(persisted.Id, Is.EqualTo(rate.Id));

        Assert.That(
            logger.Entries.Any(e => e.Message.Contains(MultiCurrencyAuditActions.ExchangeRateCreated)),
            Is.True, "Create should emit an audit entry.");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task CreateAsync_NonPositiveBuy_Rejected(decimal buy)
    {
        var dbName = $"rates-buy-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCurrenciesAsync(ctx);
        var service = new ExchangeRateService(
            new ExchangeRateRepository(ctx), new RecordingLogger<ExchangeRateService>());

        var ex = Assert.ThrowsAsync<UserFacingException>(async () =>
            await service.CreateAsync(CurrencyCode.Usd, CurrencyCode.Crc, buy, 525m, Past(5), "actor"));
        Assert.That(ex!.Code, Is.EqualTo(UserFacingErrorCode.OperationRejected));
    }

    [TestCase(0)]
    [TestCase(-2.5)]
    public async Task CreateAsync_NonPositiveSell_Rejected(decimal sell)
    {
        var dbName = $"rates-sell-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCurrenciesAsync(ctx);
        var service = new ExchangeRateService(
            new ExchangeRateRepository(ctx), new RecordingLogger<ExchangeRateService>());

        var ex = Assert.ThrowsAsync<UserFacingException>(async () =>
            await service.CreateAsync(CurrencyCode.Usd, CurrencyCode.Crc, 520m, sell, Past(5), "actor"));
        Assert.That(ex!.Code, Is.EqualTo(UserFacingErrorCode.OperationRejected));
    }

    [Test]
    public async Task CreateAsync_FutureDated_RejectedWithFr007a()
    {
        var dbName = $"rates-future-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCurrenciesAsync(ctx);
        var service = new ExchangeRateService(
            new ExchangeRateRepository(ctx), new RecordingLogger<ExchangeRateService>());

        var future = DateTime.UtcNow.AddHours(1);
        var ex = Assert.ThrowsAsync<UserFacingException>(async () =>
            await service.CreateAsync(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, future, "actor"));
        Assert.That(ex!.Code, Is.EqualTo(UserFacingErrorCode.FutureDatedRateRejected));
    }

    [Test]
    public async Task CreateAsync_DuplicateTimestamp_MapsToFr007Code()
    {
        // The InMemory provider does not enforce UQ_ExchangeRates_PairAt; route
        // through a stub repository that throws DuplicateRateTimestampException
        // exactly like the production repo does on SQL 2627/2601.
        var stubRepo = new DuplicatingRepository();
        var logger = new RecordingLogger<ExchangeRateService>();
        var service = new ExchangeRateService(stubRepo, logger);

        var ex = Assert.ThrowsAsync<UserFacingException>(async () =>
            await service.CreateAsync(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, Past(5), "actor"));
        Assert.That(ex!.Code, Is.EqualTo(UserFacingErrorCode.DuplicateRateTimestamp));
    }

    [Test]
    public async Task RecordEditAttemptAsync_EmitsBlockedAuditEntry()
    {
        var dbName = $"rates-editblocked-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCurrenciesAsync(ctx);
        var logger = new RecordingLogger<ExchangeRateService>();
        var service = new ExchangeRateService(new ExchangeRateRepository(ctx), logger);

        await service.RecordEditAttemptAsync(Guid.NewGuid(), "actor-1");

        Assert.That(
            logger.Entries.Any(e => e.Message.Contains(MultiCurrencyAuditActions.ExchangeRateEditAttemptBlocked)),
            Is.True);
    }

    [Test]
    public async Task RecordDeleteAttemptAsync_EmitsBlockedAuditEntry()
    {
        var dbName = $"rates-delblocked-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedCurrenciesAsync(ctx);
        var logger = new RecordingLogger<ExchangeRateService>();
        var service = new ExchangeRateService(new ExchangeRateRepository(ctx), logger);

        await service.RecordDeleteAttemptAsync(Guid.NewGuid(), "actor-1");

        Assert.That(
            logger.Entries.Any(e => e.Message.Contains(MultiCurrencyAuditActions.ExchangeRateDeleteAttemptBlocked)),
            Is.True);
    }

    /// <summary>
    /// Stub <see cref="IExchangeRateRepository"/> that always throws
    /// <see cref="DuplicateRateTimestampException"/> from <c>AddAsync</c>, mirroring
    /// the real repo's translation of SQL 2627/2601 errors.
    /// </summary>
    private sealed class DuplicatingRepository : IExchangeRateRepository
    {
        public Task<ExchangeRate?> GetLatestAsync(CurrencyCode source, CurrencyCode target, CancellationToken ct = default)
            => Task.FromResult<ExchangeRate?>(null);

        public Task<IReadOnlyList<ExchangeRate>> ListByPairAsync(CurrencyCode source, CurrencyCode target, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExchangeRate>>(Array.Empty<ExchangeRate>());

        public Task AddAsync(ExchangeRate rate, CancellationToken ct = default)
            => throw new DuplicateRateTimestampException("duplicate");

        public Task<ExchangeRate?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<ExchangeRate?>(null);
    }
}
