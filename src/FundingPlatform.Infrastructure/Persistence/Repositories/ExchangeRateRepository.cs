using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// Spec 015 — EF-backed repository for <see cref="ExchangeRate"/>. Hot path
/// queries the latest rate by pair (covered by IX_ExchangeRates_PairEffectiveAtDesc).
/// </summary>
public class ExchangeRateRepository : IExchangeRateRepository
{
    private const int SqlErrorViolationOfUniqueIndex = 2601;
    private const int SqlErrorViolationOfUniqueKey = 2627;

    private readonly AppDbContext _context;

    public ExchangeRateRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<ExchangeRate?> GetLatestAsync(
        CurrencyCode source,
        CurrencyCode target,
        CancellationToken ct = default)
    {
        // Compare on the CurrencyCode value-converted property directly so EF can
        // translate to a SQL `WHERE SourceCurrencyCode = @p0`. Reaching through the
        // record's .Value is only translatable on the InMemory provider (which
        // does client-side evaluation); SQL Server fails to translate it.
        return _context.ExchangeRates
            .Where(r => r.SourceCurrency == source && r.TargetCurrency == target)
            .OrderByDescending(r => r.EffectiveAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ExchangeRate>> ListByPairAsync(
        CurrencyCode source,
        CurrencyCode target,
        CancellationToken ct = default)
    {
        var list = await _context.ExchangeRates
            .Where(r => r.SourceCurrency == source && r.TargetCurrency == target)
            .OrderByDescending(r => r.EffectiveAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list;
    }

    public async Task AddAsync(ExchangeRate rate, CancellationToken ct = default)
    {
        _context.ExchangeRates.Add(rate);
        try
        {
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            throw new DuplicateRateTimestampException(
                $"A rate for {rate.SourceCurrency}->{rate.TargetCurrency} at {rate.EffectiveAtUtc:O} already exists.",
                ex);
        }
    }

    public Task<ExchangeRate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.ExchangeRates.FirstOrDefaultAsync(r => r.Id == id, ct);

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException sql
        && (sql.Number == SqlErrorViolationOfUniqueKey || sql.Number == SqlErrorViolationOfUniqueIndex);
}
