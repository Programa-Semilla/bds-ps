// Spec 021 — see specs/021-feedback-session-may13/tasks.md T135 and research.md R-12.

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 021 / US6 / T135 / FR-032 / SC-010 (R-12) — narrative KPI counter
/// seam consumed by <see cref="IAdminDashboardProjection"/>. Lives in the
/// Application layer; the EF Core implementation
/// (<c>AdminDashboardCountersReader</c>) is bound in Infrastructure DI.
///
/// <para>
/// Both methods are degrade-to-zero candidates inside the projection
/// (R-2 — sub-projection failures fold to <c>0</c> with a WARN log).
/// </para>
/// </summary>
public interface IAdminDashboardCountersReader
{
    /// <summary>
    /// Spec 021 / FR-032 — distinct applicants who own at least one
    /// non-soft-deleted Application created in the last 12 months. The
    /// soft-delete predicate composes via <c>IApplicationQueryFilter.ExcludeDeleted</c>
    /// (FR-021 / R-10) so a single deleted-defect path never leaks into the
    /// count.
    /// </summary>
    Task<int> CountPersonasActivasAsync(CancellationToken ct);

    /// <summary>
    /// Spec 021 / FR-032 — sum of executed <c>FundingAgreement</c> disbursement
    /// amounts, derived from the converted-CRC quotation amount of each
    /// approved Item's selected supplier whose owning Application is in
    /// <c>ApplicationState.AgreementExecuted</c> AND has a FundingAgreement
    /// row. Returned in CRC.
    /// </summary>
    Task<decimal> SumFondosEntregadosAsync(CancellationToken ct);
}
