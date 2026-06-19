// Spec 041 — see specs/041-evidence-inbox/contracts/interfaces.md and data-model.md.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.EvidenceInbox;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>
/// Spec 041 — EF implementation of the funds-usage evidence inbox. One query:
/// <c>State == AgreementExecuted</c> ∧ <c>Group.Process.Status == Active</c> ∧
/// group-overlap (admin short-circuit), with <c>ExcludeDeleted</c> +
/// <c>ExcludeArchivedFund</c> applied for consistency with other reviewer reads.
/// Mirrors <see cref="ReviewerDashboardProjection"/> (placement/style) and
/// <c>SignedUploadRepository.GetPendingInboxAsync</c> (group-overlap + empty-group
/// early return + cap).
/// </summary>
public sealed class EvidenceInboxProjection : IEvidenceInboxProjection
{
    /// <summary>Cap mirrors the reviewer queue; no pagination this iteration (spec 041 Out of Scope).</summary>
    private const int MaxRows = 200;

    private readonly AppDbContext _db;
    private readonly IApplicationQueryFilter _queryFilter;

    public EvidenceInboxProjection(AppDbContext db, IApplicationQueryFilter queryFilter)
    {
        _db = db;
        _queryFilter = queryFilter;
    }

    public async Task<IReadOnlyList<EvidenceInboxRowDto>> GetForUserAsync(IReviewerScope scope, CancellationToken ct)
    {
        // FR-002 — a non-admin reviewer with no group memberships sees an empty
        // inbox (mirrors SignedUploadRepository.GetPendingInboxAsync early return).
        var isAdmin = scope.IsAdmin;
        var groupIds = scope.GroupIds.ToList();
        if (!isAdmin && groupIds.Count == 0)
        {
            return Array.Empty<EvidenceInboxRowDto>();
        }

        // Consistency with other reviewer reads: drop soft-deleted and archived-fund apps.
        var apps = _queryFilter.ExcludeArchivedFund(
            _queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking()));

        // Single statement (NFR-001 — scoping enforced in-query, not in the view).
        var query =
            from app in apps
            where app.State == ApplicationState.AgreementExecuted
                && app.Group != null
                && app.Group.Process != null
                && app.Group.Process.Status == ProcessStatus.Active   // FR-004 — closed excluded, live
                && (isAdmin
                    || _db.UserGroupMemberships.Any(m =>
                        m.UserId == app.Applicant.UserId && groupIds.Contains(m.GroupId)))
            orderby app.UpdatedAt descending                          // D8 — most-recently-executed first
            select new
            {
                app.Id,
                app.Applicant.FirstName,
                app.Applicant.LastName,
                FundName = app.Group!.Process!.Fund!.Name,
                ProcessName = app.Group!.Process!.Name,
                app.UpdatedAt,
            };

        var rows = await query.Take(MaxRows).ToListAsync(ct);

        // Format APP-{id:D5} and the display name in memory (not SQL-translatable).
        return rows
            .Select(r => new EvidenceInboxRowDto(
                ApplicationId: r.Id,
                ApplicationNumber: $"APP-{r.Id:D5}",
                ApplicantName: BuildApplicantName(r.FirstName, r.LastName),
                FundName: r.FundName ?? string.Empty,
                ProcessName: r.ProcessName ?? string.Empty,
                ExecutedAtUtc: new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>"Solicitante" fallback for an empty name (mirrors the reviewer queue, FR-003).</summary>
    private static string BuildApplicantName(string firstName, string lastName)
    {
        var full = $"{firstName} {lastName}".Trim();
        return string.IsNullOrEmpty(full) ? "Solicitante" : full;
    }
}
