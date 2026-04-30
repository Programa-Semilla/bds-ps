using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly AppDbContext _context;

    public SupplierRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Supplier?> GetByLegalIdAsync(string legalId)
    {
        var canonical = Supplier.NormalizeLegalId(legalId);
        return await _context.Suppliers.FirstOrDefaultAsync(s => s.LegalId == canonical);
    }

    public async Task<Supplier?> GetByLegalIdWithBranchesAsync(string legalId)
    {
        var canonical = Supplier.NormalizeLegalId(legalId);
        return await _context.Suppliers
            .Include(s => s.Branches)
            .FirstOrDefaultAsync(s => s.LegalId == canonical);
    }

    public async Task AddAsync(Supplier supplier)
    {
        await _context.Suppliers.AddAsync(supplier);
    }

    public async Task<Supplier?> GetByIdAsync(int id)
    {
        return await _context.Suppliers.FindAsync(id);
    }

    public async Task<Supplier?> GetByIdWithBranchesAsync(int id)
    {
        return await _context.Suppliers
            .Include(s => s.Branches)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<(IReadOnlyList<Supplier> Items, int Total)> ListForAdminAsync(
        SupplierAdminFilter filter, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        var query = _context.Suppliers.Include(s => s.Branches).AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(s => s.VerificationStatus == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.LegalIdContains))
        {
            // Normalize for case-insensitive contains. EF Core translates to LIKE.
            var needle = filter.LegalIdContains.Trim().ToUpperInvariant();
            query = query.Where(s => s.LegalId.Contains(needle));
        }

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
        {
            var needle = filter.NameContains.Trim();
            query = query.Where(s => EF.Functions.Like(s.Name, $"%{needle}%"));
        }

        if (filter.HasIncompleteCompliance == true)
        {
            query = query.Where(s =>
                !s.IsCompliantCCSS ||
                !s.IsCompliantHacienda ||
                !s.IsCompliantSICOP ||
                !s.HasElectronicInvoice);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountReferencingApplicationsAsync(int supplierId)
    {
        // Count distinct applications whose items have a quotation referencing this supplier.
        return await _context.Quotations
            .Where(q => q.SupplierId == supplierId)
            .Join(_context.Set<Item>(),
                q => q.ItemId,
                i => i.Id,
                (q, i) => i.ApplicationId)
            .Distinct()
            .CountAsync();
    }

    public Task UpdateAsync(Supplier supplier)
    {
        _context.Suppliers.Update(supplier);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        return _context.SaveChangesAsync();
    }
}
