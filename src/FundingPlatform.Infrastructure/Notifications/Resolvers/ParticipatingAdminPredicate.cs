using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Notifications.Resolvers;

/// <summary>
/// Spec 021 / T031 / FR-013 / R-006 — encapsulates the v1 participating-admin
/// predicate: a user qualifies as a participating admin for an application
/// when (a) they appear in <c>VersionHistory.UserId</c> for that application
/// AND (b) they currently hold the "Admin" role.
///
/// <para>
/// Known limitation (EC-002 / OQ-011): a user who acted as admin in the past
/// and was later demoted to reviewer will NOT match the predicate. A future
/// spec MAY add a <c>VersionHistory.RoleAtAction</c> snapshot column to
/// restore full EC-002 fidelity. v1 ships the over-narrow predicate by
/// design — the matching integration tests in T078 mark the demoted-admin
/// subcase as <c>[Explicit]</c> until the predicate is extended.
/// </para>
/// </summary>
public sealed class ParticipatingAdminPredicate
{
    private readonly AppDbContext _context;

    public ParticipatingAdminPredicate(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Returns the deterministic set of user ids matching the v1 predicate.
    /// Composed at the EF query level so the role join executes server-side.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetParticipatingAdminUserIdsAsync(
        int applicationId, CancellationToken ct)
    {
        var query =
            from vh in _context.VersionHistories
            where vh.ApplicationId == applicationId
            join uRole in _context.UserRoles on vh.UserId equals uRole.UserId
            join role in _context.Roles on uRole.RoleId equals role.Id
            where role.NormalizedName == "ADMIN"
            select vh.UserId;

        return await query.Distinct().OrderBy(id => id).ToListAsync(ct);
    }
}
