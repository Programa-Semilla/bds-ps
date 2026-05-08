using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>
/// Spec 017 / US7 — reads the most recent <see cref="AdminAuditEvent"/> rows
/// for the dashboard activity feed. AsNoTracking + ordered by
/// <c>OccurredAt desc</c> so the projection sees newest events first.
/// </summary>
public sealed class AdminAuditEventReader : IAdminAuditEventReader
{
    private readonly AppDbContext _db;

    public AdminAuditEventReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AdminAuditEvent>> GetRecentAsync(int take, TimeSpan window, CancellationToken ct)
    {
        if (take <= 0)
        {
            return Array.Empty<AdminAuditEvent>();
        }
        var since = DateTimeOffset.UtcNow - window;
        return await _db.AdminAuditEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .OrderByDescending(e => e.OccurredAt)
            .Take(take)
            .ToListAsync(ct);
    }
}
