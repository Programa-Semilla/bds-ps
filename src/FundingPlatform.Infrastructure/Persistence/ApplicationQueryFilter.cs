// Spec 021 — see specs/021-feedback-session-may13/research.md R-10.

using FundingPlatform.Application.Abstractions;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>
/// Spec 021 / FR-021 / R-10 — single centralised soft-delete predicate
/// (<c>DeletedAt IS NULL</c>) applied by every projection / read path that
/// touches <c>dbo.Applications</c>. Hosted in Infrastructure so the
/// Application layer composes the filter through
/// <see cref="IApplicationQueryFilter"/> without importing EF Core.
///
/// The polish-phase task (T152 + <c>DashboardQueriesHonorSoftDeleteTests</c>)
/// audits every <c>_db.Applications.AsQueryable()</c> call site to confirm it
/// routes through this seam. See <c>specs/021-feedback-session-may13/research.md</c>
/// R-10 for the audit rationale.
/// </summary>
public sealed class ApplicationQueryFilter : IApplicationQueryFilter
{
    /// <inheritdoc />
    public IQueryable<AppEntity> ExcludeDeleted(IQueryable<AppEntity> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Where(a => a.DeletedAt == null);
    }
}
