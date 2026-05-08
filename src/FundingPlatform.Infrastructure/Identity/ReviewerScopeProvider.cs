using FundingPlatform.Application.Reviewer;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Identity;

/// <summary>
/// Spec 016 — resolves the reviewer's group ids from the database fresh per
/// request (NFR-003: membership changes take effect on the next request, no
/// sign-out required). Admin callers short-circuit and never hit the DB.
/// </summary>
public sealed class ReviewerScopeProvider : IReviewerScopeProvider
{
    private readonly AppDbContext _db;

    public ReviewerScopeProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReviewerScope> GetForUserAsync(string userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin)
        {
            return ReviewerScope.Admin;
        }
        if (string.IsNullOrEmpty(userId))
        {
            return ReviewerScope.Empty;
        }
        var groupIds = await _db.UserGroupMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);
        return new ReviewerScope(false, groupIds);
    }
}
