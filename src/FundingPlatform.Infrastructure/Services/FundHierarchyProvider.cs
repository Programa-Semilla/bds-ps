using FundingPlatform.Application.Admin.Filters;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IFundHierarchyProvider"/>. Three small round-trips
/// (Funds, Processes, Groups) assembled in memory so a Fund or Process with zero
/// children still appears — an inner join would hide those.
/// </summary>
public sealed class FundHierarchyProvider : IFundHierarchyProvider
{
    private readonly AppDbContext _db;

    public FundHierarchyProvider(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<FundHierarchyNode>> GetAsync(bool includeArchived, CancellationToken ct)
    {
        var fundsQuery = _db.Funds.AsNoTracking();
        if (!includeArchived)
        {
            fundsQuery = fundsQuery.Where(f => f.Status == FundStatus.Active);
        }

        var funds = await fundsQuery
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        var processes = await _db.Processes.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.FundId })
            .ToListAsync(ct);

        var groups = await _db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.ProcessId })
            .ToListAsync(ct);

        return funds
            .Select(f => new FundHierarchyNode(
                f.Id,
                f.Name,
                processes
                    .Where(p => p.FundId == f.Id)
                    .Select(p => new ProcessHierarchyNode(
                        p.Id,
                        p.Name,
                        groups
                            .Where(g => g.ProcessId == p.Id)
                            .Select(g => new GroupHierarchyNode(g.Id, g.Name))
                            .ToList()))
                    .ToList()))
            .ToList();
    }
}
