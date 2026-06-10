// Spec 021 — see specs/021-feedback-session-may13/tasks.md T136 and research.md R-12.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.ReviewerDashboard;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>
/// Spec 021 / US6 / T136 / FR-033 / SC-010 (R-12) — EF implementation of the
/// reviewer-dashboard projection. The "pending" tile that previously lived on
/// the admin dashboard now renders here; single source of truth. Evolved
/// 2026-05-25 to count Submitted *applications* rather than individual
/// quotations.
/// </summary>
public sealed class ReviewerDashboardProjection : IReviewerDashboardProjection
{
    private readonly AppDbContext _db;
    private readonly IApplicationQueryFilter _queryFilter;

    public ReviewerDashboardProjection(AppDbContext db, IApplicationQueryFilter queryFilter)
    {
        _db = db;
        _queryFilter = queryFilter;
    }

    public Task<int> CountPendingApplicationsAsync(CancellationToken ct)
    {
        // Pending review work = non-soft-deleted Applications in Submitted state
        // (not-yet-picked-up). Count APPLICATIONS, not quotations: an application
        // with several competing quotes on an item is one unit of pending work,
        // not many (FR-033 evolution 2026-05-25). UnderReview is reviewer-active,
        // so it is excluded from the *pending* set.
        // Spec 029 / FR-020 — archived-Fund applications drop off reviewer widgets.
        return _queryFilter.ExcludeArchivedFund(
                _queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking()))
            .Where(a => a.State == ApplicationState.Submitted)
            .CountAsync(ct);
    }
}
