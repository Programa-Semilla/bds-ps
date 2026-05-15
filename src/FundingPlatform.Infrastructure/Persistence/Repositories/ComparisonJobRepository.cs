using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence.Repositories;

/// <summary>Spec 020 / FR-F1..FR-F3 — EF-backed comparison-job persistence.</summary>
public class ComparisonJobRepository : IComparisonJobRepository
{
    private readonly AppDbContext _context;

    public ComparisonJobRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<ComparisonJob?> GetAsync(Guid id, CancellationToken ct)
        => _context.ComparisonJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IReadOnlyList<ComparisonJob>> GetPendingForApplicationAsync(
        int applicationId, CancellationToken ct)
    {
        // ApplicationItem.ApplicationId join via Items
        var rows = await (
            from j in _context.ComparisonJobs
            join i in _context.Items on j.ApplicationItemId equals i.Id
            where i.ApplicationId == applicationId
                  && (j.Status == ComparisonJobStatus.Pending
                      || j.Status == ComparisonJobStatus.Running)
            select j).ToListAsync(ct);
        return rows;
    }

    public Task<ComparisonJob?> GetByApplicationItemAsync(int applicationItemId, CancellationToken ct)
        => _context.ComparisonJobs
            .Where(j => j.ApplicationItemId == applicationItemId
                        && (j.Status == ComparisonJobStatus.Pending
                            || j.Status == ComparisonJobStatus.Running))
            .OrderByDescending(j => j.LastStatusChangeAt)
            .FirstOrDefaultAsync(ct);

    public async Task EnqueueAsync(ComparisonJob job, CancellationToken ct)
    {
        _context.ComparisonJobs.Add(job);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ComparisonJob job, CancellationToken ct)
    {
        _context.ComparisonJobs.Update(job);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<ComparisonJob?> ClaimNextPendingAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Pessimistic claim: walk pending rows in FIFO order, attempt to flip
        // each to Running with concurrency token. SaveChanges throws on race
        // and we retry the next row. Returns null when nothing claimable.
        var candidates = await _context.ComparisonJobs
            .Where(j => j.Status == ComparisonJobStatus.Pending)
            .OrderBy(j => j.LastStatusChangeAt)
            .Take(8)
            .ToListAsync(ct);

        foreach (var job in candidates)
        {
            try
            {
                job.Start(now);
                await _context.SaveChangesAsync(ct);
                return job;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker claimed it; reload and try the next one.
                _context.Entry(job).State = EntityState.Detached;
                continue;
            }
            catch (InvalidOperationException)
            {
                // Job was no longer Pending; move on.
                _context.Entry(job).State = EntityState.Detached;
                continue;
            }
        }
        return null;
    }

    public async Task<IReadOnlyList<ComparisonJob>> GetOrphanedRunningAsync(
        DateTimeOffset cutoff, CancellationToken ct)
    {
        var rows = await _context.ComparisonJobs
            .Where(j => j.Status == ComparisonJobStatus.Running
                        && j.LastStatusChangeAt < cutoff)
            .ToListAsync(ct);
        return rows;
    }

    public Task<ComparisonJob?> GetLatestByApplicationItemAsync(int applicationItemId, CancellationToken ct)
        => _context.ComparisonJobs
            .Where(j => j.ApplicationItemId == applicationItemId)
            .OrderByDescending(j => j.LastStatusChangeAt)
            .FirstOrDefaultAsync(ct);
}
