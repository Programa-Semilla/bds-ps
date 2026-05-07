using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// Spec 015 — EF-backed repository for the <c>Currencies</c> catalog.
/// </summary>
public class CurrencyRepository : ICurrencyRepository
{
    private readonly AppDbContext _context;

    public CurrencyRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<Currency?> GetByCodeAsync(CurrencyCode code, CancellationToken ct = default)
    {
        return _context.Currencies.FirstOrDefaultAsync(c => c.Code == code, ct);
    }

    public async Task<IReadOnlyList<Currency>> ListAllAsync(CancellationToken ct = default)
    {
        var rows = await _context.Currencies
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyList<Currency>> ListEnabledAsync(CancellationToken ct = default)
    {
        var rows = await _context.Currencies
            .AsNoTracking()
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);
}
