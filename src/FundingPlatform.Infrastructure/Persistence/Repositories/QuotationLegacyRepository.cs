using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// Spec 015 / US6 — EF-backed repository for the legacy-quotation review queue.
/// Backed by the <c>IX_Quotations_LegacyNeedsReview</c> filtered index on
/// <c>LegacyNeedsReview = 1</c>. The flagged set is small in practice (only
/// pre-spec-015 non-CRC quotations) so the join across Items + Suppliers stays
/// cheap.
/// </summary>
public class QuotationLegacyRepository : IQuotationLegacyRepository
{
    private readonly AppDbContext _context;

    public QuotationLegacyRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<Quotation?> GetByIdAsync(int quotationId, CancellationToken ct = default)
        => _context.Quotations.FirstOrDefaultAsync(q => q.Id == quotationId, ct);

    public async Task<IReadOnlyList<LegacyQuotationRow>> ListFlaggedAsync(CancellationToken ct = default)
    {
        var rows = await _context.Quotations
            .AsNoTracking()
            .Where(q => q.LegacyNeedsReview)
            .Join(_context.Items, q => q.ItemId, i => i.Id, (q, i) => new { q, i })
            .Join(_context.Suppliers, x => x.q.SupplierId, s => s.Id, (x, s) => new { x.q, x.i, s })
            .OrderBy(x => x.q.CreatedAt)
            .Select(x => new LegacyQuotationRow(
                x.q.Id,
                x.i.ApplicationId,
                x.i.Id,
                x.i.ProductName,
                x.s.Name,
                x.q.Price,
                x.q.Currency,
                x.q.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);
}
