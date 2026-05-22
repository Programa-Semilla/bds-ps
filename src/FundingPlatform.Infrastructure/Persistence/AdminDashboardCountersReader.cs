// Spec 021 — see specs/021-feedback-session-may13/tasks.md T135 and research.md R-12.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>
/// Spec 021 / US6 / T135 / FR-032 / FR-021 / SC-010 (R-12) — EF-backed reader
/// for the two narrative KPI counters on the admin dashboard. Composes the
/// soft-delete predicate via <see cref="IApplicationQueryFilter.ExcludeDeleted"/>
/// (R-10) so the "deleted-still-active" defect path can never leak into the
/// Personas activas count.
/// </summary>
public sealed class AdminDashboardCountersReader : IAdminDashboardCountersReader
{
    /// <summary>Spec 021 / FR-032 — active applicant window is the trailing 12 months.</summary>
    public static readonly TimeSpan PersonasActivasWindow = TimeSpan.FromDays(365);

    private readonly AppDbContext _db;
    private readonly IApplicationQueryFilter _queryFilter;
    private readonly TimeProvider _clock;

    public AdminDashboardCountersReader(
        AppDbContext db,
        IApplicationQueryFilter queryFilter,
        TimeProvider? clock = null)
    {
        _db = db;
        _queryFilter = queryFilter;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<int> CountPersonasActivasAsync(CancellationToken ct)
    {
        var since = _clock.GetUtcNow().UtcDateTime - PersonasActivasWindow;

        var apps = _queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking())
            .Where(a => a.CreatedAt >= since);

        return apps.Select(a => a.ApplicantId).Distinct().CountAsync(ct);
    }

    public async Task<decimal> SumFondosEntregadosAsync(CancellationToken ct)
    {
        // Sum the converted-CRC quotation amount of each approved Item's
        // selected supplier whose owning Application is in AgreementExecuted
        // state. Soft-deleted Applications are excluded via
        // IApplicationQueryFilter. The state alone is sufficient — every
        // AgreementExecuted Application has, by construction, an attached
        // FundingAgreement (the state transition is gated on
        // GenerateFundingAgreement succeeding).
        var apps = _queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking())
            .Where(a => a.State == ApplicationState.AgreementExecuted);

        var query =
            from a in apps
            join i in _db.Items.AsNoTracking() on a.Id equals i.ApplicationId
            join q in _db.Quotations.AsNoTracking()
                on new { ItemId = i.Id, SupplierId = i.SelectedSupplierId!.Value }
                equals new { ItemId = q.ItemId, SupplierId = q.SupplierId }
            where i.ReviewStatus == ItemReviewStatus.Approved
               && i.SelectedSupplierId != null
            select q.ConvertedCrcAmount;

        // SumAsync over nullable decimal projects NULL as 0 in SQL; we want
        // the same here for CRC totals.
        var total = await query.SumAsync(x => x ?? 0m, ct);
        return total;
    }
}
