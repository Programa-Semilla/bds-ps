// Spec 021 — see specs/021-feedback-session-may13/tasks.md T136 and research.md R-12.

namespace FundingPlatform.Application.ReviewerDashboard;

/// <summary>
/// Spec 021 / US6 / T136 / FR-033 / SC-010 (R-12) — reviewer-dashboard
/// projection. Holds the pending-quotation tile that moved off the admin
/// dashboard per FR-033 (single source of truth — the admin surface no
/// longer projects this counter).
///
/// <para>
/// The implementation
/// (<c>FundingPlatform.Infrastructure.Persistence.ReviewerDashboardProjection</c>)
/// reads <c>dbo.Quotations</c> joined to <c>dbo.Applications</c> filtered
/// to <c>State == Submitted</c> (i.e. awaiting reviewer action) and excludes
/// soft-deleted Applications via <see cref="Abstractions.IApplicationQueryFilter"/>
/// (FR-021 / R-10).
/// </para>
/// </summary>
public interface IReviewerDashboardProjection
{
    /// <summary>
    /// Spec 021 / FR-033 — count of quotations attached to non-soft-deleted
    /// Applications in <c>ApplicationState.Submitted</c> (i.e. quotations
    /// whose owning Application is awaiting reviewer action). Returns 0
    /// when the Quotations table is empty or no Application is in the
    /// Submitted state.
    /// </summary>
    Task<int> CountPendingQuotationsAsync(CancellationToken ct);
}
