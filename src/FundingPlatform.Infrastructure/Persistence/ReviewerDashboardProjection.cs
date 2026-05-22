// Spec 021 — see specs/021-feedback-session-may13/tasks.md T136 and research.md R-12.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.ReviewerDashboard;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>
/// Spec 021 / US6 / T136 / FR-033 / SC-010 (R-12) — EF implementation of the
/// reviewer-dashboard projection. The pending-quotation tile that previously
/// lived on the admin dashboard now renders here; single source of truth.
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

    public Task<int> CountPendingQuotationsAsync(CancellationToken ct)
    {
        // Quotations awaiting reviewer action = quotations on Items belonging
        // to a non-soft-deleted Application in Submitted state. UnderReview is
        // a reviewer-active state but the queue counter (FR-033 / R-12) names
        // the *pending* set, i.e. not-yet-picked-up.
        var apps = _queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking())
            .Where(a => a.State == ApplicationState.Submitted);

        var query =
            from q in _db.Quotations.AsNoTracking()
            join i in _db.Items.AsNoTracking() on q.ItemId equals i.Id
            join a in apps on i.ApplicationId equals a.Id
            select q.Id;

        return query.CountAsync(ct);
    }
}
