// Spec 041 — see specs/041-evidence-inbox/contracts/interfaces.md and data-model.md.

using FundingPlatform.Application.Reviewer;

namespace FundingPlatform.Application.EvidenceInbox;

/// <summary>
/// Spec 041 — projects the funds-usage evidence inbox: executed applications
/// (<c>AgreementExecuted</c>) whose governing Process is <c>Active</c>, scoped to
/// the caller. Mirrors <c>IReviewerDashboardProjection</c> (Application-resident
/// interface, Infrastructure-resident EF impl) and the signing-inbox query shape.
/// Group-overlap is enforced in-query (NFR-001), not by UI filtering.
/// </summary>
public interface IEvidenceInboxProjection
{
    /// <summary>
    /// Executed applications whose governing Process is <c>Active</c>, scoped to
    /// the caller: an admin short-circuits to all; otherwise the applicant must
    /// share at least one group with the reviewer. A non-admin reviewer with no
    /// group memberships gets an empty list (FR-002). Most-recently-executed
    /// first, capped (no pagination this iteration).
    /// </summary>
    Task<IReadOnlyList<EvidenceInboxRowDto>> GetForUserAsync(IReviewerScope scope, CancellationToken ct);
}

/// <summary>
/// Spec 041 — one inbox row. Produced by <see cref="IEvidenceInboxProjection"/>;
/// never persisted. Carries just enough to identify and open an application
/// (FR-003): its number, the applicant, and the fund/process it belongs to.
/// </summary>
public sealed record EvidenceInboxRowDto(
    int ApplicationId,
    string ApplicationNumber,
    string ApplicantName,
    string FundName,
    string ProcessName,
    DateTimeOffset ExecutedAtUtc);
