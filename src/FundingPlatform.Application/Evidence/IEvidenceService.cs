using FundingPlatform.Application.Admin.Users.DTOs;

namespace FundingPlatform.Application.Evidence;

/// <summary>
/// Spec 047 — orchestrates the evidence-graph lifecycle (list/detail/attach/replace/allocate/
/// delete/download) for one executed application. The caller (controller) owns group-scope + role
/// authorization and runs the size guard + file-type policy at the HTTP boundary; this service
/// trusts the caller for scope, exactly like the shipped <c>DisbursementService</c>. <c>Result</c>/
/// <c>Result&lt;T&gt;</c> surface refusals (orphan guard, allocation integrity, closure lock,
/// reason-required); optimistic-concurrency conflicts surface as a retryable es-CR error.
/// </summary>
public interface IEvidenceService
{
    /// <summary>Flat, group-agnostic read ordered newest-first. Authorization is the controller's job.</summary>
    Task<IReadOnlyList<EvidenceSummary>> ListForApplicationAsync(int applicationId, CancellationToken ct);

    /// <summary>Full detail incl. per-line allocations + the version chain, or null when the row
    /// does not belong to the application.</summary>
    Task<EvidenceDetail?> GetAsync(int applicationId, int evidenceId, CancellationToken ct);

    /// <summary>Resolves a BackendStream serving handle for a stored evidence document. When
    /// <paramref name="versionNumber"/> is null the current version is served; otherwise the named
    /// historical version. Null when not found.</summary>
    Task<EvidenceDownload?> OpenForDownloadAsync(int applicationId, int evidenceId, int? versionNumber, CancellationToken ct);

    /// <summary>FR-002 — attach a new evidence document (uploads blob, creates v1, enforces the
    /// orphan guard + allocation integrity). Returns the created id.</summary>
    Task<Result<int>> AttachAsync(AttachEvidenceCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>FR-021 — append a new version (reason required), superseding the prior. Refused when
    /// any target line is closed.</summary>
    Task<Result> ReplaceAsync(ReplaceEvidenceCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>FR-003 — replace-all the evidence's per-line allocation rows (Σ ≤ amount). Refused
    /// when any target line is closed.</summary>
    Task<Result> AllocateAsync(AllocateEvidenceCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>FR-007 — delete an evidence document (pre-close only; blob best-effort cleanup).</summary>
    Task<Result> DeleteAsync(int applicationId, int evidenceId, string actorUserId, CancellationToken ct);
}
