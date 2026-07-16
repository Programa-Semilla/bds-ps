using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Disbursements;

/// <summary>
/// Spec 045 — orchestrates the disbursement lifecycle (list/detail/record/edit/
/// attach-evidence/validate/cancel/download). The caller (controller) owns group-scope +
/// role authorization and runs the size guard + file-type policy at the HTTP boundary;
/// this service trusts the caller for scope, exactly like the shipped Fund/FundsUsageEvidence
/// services. <c>Result</c>/<c>Result&lt;T&gt;</c> collect all validation errors at once
/// (Constitution Quality Gate); optimistic-concurrency conflicts surface as a retryable
/// es-CR error.
/// </summary>
public interface IDisbursementService
{
    /// <summary>Flat, group-agnostic read ordered newest-first. Authorization is the controller's job.</summary>
    Task<IReadOnlyList<DisbursementListItem>> ListAsync(int applicationId, CancellationToken ct);

    /// <summary>Full detail with the live (recomputed) discrepancy list, or null when the row
    /// does not belong to the application.</summary>
    Task<DisbursementDetail?> GetAsync(int applicationId, int disbursementId, CancellationToken ct);

    /// <summary>FR-001 — record a disbursement (executed-gate + amount&gt;0 + CRC). Posts the
    /// one-time Allocation ledger entry if none exists (idempotent). Returns the created id.</summary>
    Task<Result<int>> RecordAsync(RecordDisbursementCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>FR-028 — edit a pre-validation disbursement; re-runs reconciliation (FR-016).</summary>
    Task<Result> EditAsync(EditDisbursementCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>FR-006/FR-010 — create-or-replace the evidence for the given Kind; re-runs
    /// reconciliation. Returns the evidence row id.</summary>
    Task<Result<int>> AttachEvidenceAsync(AttachDisbursementEvidenceCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>FR-026/FR-027 — the explicit Validar action, gated on both evidences present AND
    /// zero discrepancies AND (race-proof) the committed Σ not breaching the allocation. On pass
    /// flips State=Validated and posts the immutable Disbursement ledger entry.</summary>
    Task<Result> ValidateAsync(int applicationId, int disbursementId, string actorUserId, CancellationToken ct);

    /// <summary>FR-028 — cancel a pre-validation disbursement (guarded Recorded/Inconsistent).</summary>
    Task<Result> CancelAsync(int applicationId, int disbursementId, string actorUserId, CancellationToken ct);

    /// <summary>Resolves a BackendStream serving handle for a stored evidence document, or null.</summary>
    Task<DisbursementEvidenceDownload?> OpenEvidenceForDownloadAsync(
        int applicationId, int disbursementId, EvidenceKind kind, CancellationToken ct);
}
