// Spec 021 — see specs/021-feedback-session-may13/research.md R-10.

using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Abstractions;

/// <summary>
/// Spec 021 / FR-021 / R-10 — single centralised seam for the soft-delete
/// predicate (<c>DeletedAt IS NULL</c>) applied to every dashboard /
/// reviewer-queue / signing-inbox / counter query touching the
/// <c>dbo.Applications</c> table.
///
/// Implementations live in Infrastructure (<c>ApplicationQueryFilter</c>) so the
/// Application layer can call <c>filter.ExcludeDeleted(query)</c> without
/// importing EF Core. The polish phase audits all read paths to confirm
/// every <c>_db.Applications.AsQueryable()</c> call routes through this seam
/// (see T152 + <c>DashboardQueriesHonorSoftDeleteTests</c>).
/// </summary>
public interface IApplicationQueryFilter
{
    /// <summary>
    /// Returns <paramref name="source"/> filtered to non-soft-deleted rows
    /// (<c>DeletedAt IS NULL</c>). Idempotent — composing the predicate twice
    /// is a no-op at the SQL level once EF folds the duplicate <c>WHERE</c>.
    /// </summary>
    IQueryable<AppEntity> ExcludeDeleted(IQueryable<AppEntity> source);

    /// <summary>
    /// Spec 029 / FR-020 — returns <paramref name="source"/> filtered to
    /// applications whose governing Fund (via <c>Group.Process.Fund</c>) is NOT
    /// Archived. Composed alongside <see cref="ExcludeDeleted"/> at every
    /// non-admin read site so an archived Fund's applications vanish from
    /// applicant lists, the reviewer queue, the signing inbox, and reviewer
    /// counters. Admin reports deliberately do NOT apply this filter (admins
    /// retain visibility into archived Funds). Idempotent.
    /// </summary>
    IQueryable<AppEntity> ExcludeArchivedFund(IQueryable<AppEntity> source);
}
