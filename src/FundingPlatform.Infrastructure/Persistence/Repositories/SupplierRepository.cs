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

    public async Task<IReadOnlyList<Supplier>> ListByIdsWithBranchesAsync(IReadOnlyCollection<int> supplierIds)
    {
        ArgumentNullException.ThrowIfNull(supplierIds);
        if (supplierIds.Count == 0) return Array.Empty<Supplier>();
        return await _context.Suppliers
            .Include(s => s.Branches)
            .Where(s => supplierIds.Contains(s.Id))
            .ToListAsync();
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

    /// <summary>
    /// Spec 021 / US3 / T108 / FR-011 — supplier-admin list. Default sort is
    /// <c>LastUsedAt DESC</c> (NULLs last); LastUsedAt is the MAX(CreatedAt)
    /// across the supplier's <see cref="Quotation"/> rows. Optional ProcessId
    /// filter narrows the list to suppliers referenced by Applications whose
    /// Applicant belongs (via <see cref="UserGroupMembership"/>) to a Group
    /// under the given Process.
    ///
    /// <para>
    /// Performance: the query joins Quotation → Item → Application → Applicant
    /// → ApplicationUser → UserGroupMembership → Group on the ProcessId
    /// predicate. All foreign keys are indexed (existing dacpac); the
    /// LastUsedAt subquery is a correlated MAX without scanning. Hot path is
    /// the unfiltered case where we skip the Process join entirely.
    /// </para>
    /// </summary>
    public async Task<(IReadOnlyList<SupplierAdminLastUsedRow> Items, int Total)> ListForSupplierAdminAsync(
        SupplierAdminFilter filter, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        var query = _context.Suppliers.AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(s => s.VerificationStatus == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            // FR-009: single term hits Name OR LegalId.
            var pattern = $"%{filter.SearchTerm.Trim()}%";
            query = query.Where(s =>
                EF.Functions.Like(s.Name, pattern) ||
                EF.Functions.Like(s.LegalId, pattern));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(filter.LegalIdContains))
            {
                var needle = filter.LegalIdContains.Trim().ToUpperInvariant();
                query = query.Where(s => s.LegalId.Contains(needle));
            }
            if (!string.IsNullOrWhiteSpace(filter.NameContains))
            {
                var pattern = $"%{filter.NameContains.Trim()}%";
                query = query.Where(s => EF.Functions.Like(s.Name, pattern));
            }
        }

        if (filter.HasIncompleteCompliance == true)
        {
            query = query.Where(s =>
                !s.IsCompliantCCSS ||
                !s.IsCompliantHacienda ||
                !s.IsCompliantSICOP ||
                !s.HasElectronicInvoice);
        }

        if (filter.ProcessId is int procId)
        {
            // FR-011: restrict to suppliers used by Applications whose
            // Applicant's Group lives under this Process. Join chain:
            //   Quotation → Item → Application → Applicant → AspNetUsers
            //   .Id ↔ UserGroupMembership.UserId → Group.ProcessId.
            var supplierIdsInProcess =
                from q in _context.Quotations
                join i in _context.Items on q.ItemId equals i.Id
                join a in _context.Applications on i.ApplicationId equals a.Id
                join app in _context.Applicants on a.ApplicantId equals app.Id
                join m in _context.UserGroupMemberships on app.UserId equals m.UserId
                join g in _context.Groups on m.GroupId equals g.Id
                where g.ProcessId == procId
                select q.SupplierId;
            query = query.Where(s => supplierIdsInProcess.Contains(s.Id));
        }

        if (filter.FundId is int fundId)
        {
            // Restrict to suppliers used by Applications whose Applicant's Group
            // belongs to a Process under this Fund. Same join chain as the
            // Process filter, extended Group → Process → FundId.
            var supplierIdsInFund =
                from q in _context.Quotations
                join i in _context.Items on q.ItemId equals i.Id
                join a in _context.Applications on i.ApplicationId equals a.Id
                join app in _context.Applicants on a.ApplicantId equals app.Id
                join m in _context.UserGroupMemberships on app.UserId equals m.UserId
                join g in _context.Groups on m.GroupId equals g.Id
                join p in _context.Processes on g.ProcessId equals p.Id
                where p.FundId == fundId
                select q.SupplierId;
            query = query.Where(s => supplierIdsInFund.Contains(s.Id));
        }

        // Compute LastUsedAt via correlated subquery — translates to SQL Server
        // OUTER APPLY (max). Null when the supplier has no quotations yet.
        var projected = query.Select(s => new
        {
            Supplier = s,
            BranchCount = s.Branches.Count,
            LastUsedAt = (DateTime?)_context.Quotations
                .Where(q => q.SupplierId == s.Id)
                .Max(q => (DateTime?)q.CreatedAt),
        });

        var total = await projected.CountAsync();

        // FR-011: default sort LastUsedAt DESC, with NULLs last. SQL Server
        // orders NULL low ASC / high DESC by default — we explicitly emit
        // "ORDER BY (LastUsedAt IS NULL), LastUsedAt DESC".
        var page1 = await projected
            .OrderBy(x => x.LastUsedAt == null)
            .ThenByDescending(x => x.LastUsedAt)
            .ThenBy(x => x.Supplier.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var rows = page1.Select(x => new SupplierAdminLastUsedRow(
            x.Supplier.Id,
            x.Supplier.LegalId,
            x.Supplier.Name,
            x.Supplier.VerificationStatus,
            x.BranchCount,
            !x.Supplier.IsCompliantCCSS
                || !x.Supplier.IsCompliantHacienda
                || !x.Supplier.IsCompliantSICOP
                || !x.Supplier.HasElectronicInvoice,
            x.Supplier.UpdatedAt,
            x.LastUsedAt)).ToList();

        return (rows, total);
    }
}
