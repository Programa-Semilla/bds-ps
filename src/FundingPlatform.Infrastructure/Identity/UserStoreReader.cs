using FundingPlatform.Application.Services;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Identity;

/// <summary>
/// Spec 017 / US1 — minimal read-only user-store surface. Excludes the system
/// sentinel admin per spec 009 FR-019. Used by
/// <c>AdminDashboardProjection</c> for the Active-users KPI and to resolve
/// activity-feed actor display names.
/// </summary>
public sealed class UserStoreReader : IUserStoreReader
{
    private readonly AppDbContext _db;

    public UserStoreReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetActiveUserCountAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        // Sentinel exclusion is enforced by the global query filter on
        // ApplicationUser. Active = no future lockout.
        return await _db.Users
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= nowUtc)
            .CountAsync(ct);
    }

    public async Task<string> GetDisplayNameAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }
        // IgnoreQueryFilters so a deleted/sentinel actor still resolves to a
        // displayable label rather than blanking out the audit row.
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is null)
        {
            return userId;
        }
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? (user.Email ?? userId) : full;
    }
}
