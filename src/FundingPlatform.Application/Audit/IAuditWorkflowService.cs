namespace FundingPlatform.Application.Audit;

/// <summary>
/// Spec 040 — orchestrates the auditor workflow stage (Infrastructure impl). The
/// reviewer side completes the reviewer checklist and sends/re-sends to audit; the
/// auditor side records the audit checklist, approves, confirms the PDF, releases for
/// signature, or returns the application to the reviewer with per-item reasons. PDF
/// generation itself stays in the existing funding-agreement path under the re-gated
/// authorization.
/// </summary>
public interface IAuditWorkflowService
{
    // reviewer side
    Task<AuditActionResult> SubmitReviewerChecklistAndSendToAuditAsync(
        int appId, IReadOnlyList<ReviewerCheck> checks, string reviewerUserId, CancellationToken ct);

    Task<AuditActionResult> ResendToAuditAsync(
        int appId, IReadOnlyList<ReviewerCheck> checks, string reviewerUserId, CancellationToken ct);

    /// <summary>Spec 040 — the reviewer-stage checklist (+ recorded ticks) for an application,
    /// plus any auditor non-compliance findings (when returned from audit).</summary>
    Task<ReviewerChecklistView> GetReviewerChecklistAsync(int appId, CancellationToken ct);

    // auditor side
    Task<AuditChecklistView?> GetAuditChecklistAsync(int appId, CancellationToken ct);

    Task<AuditActionResult> SaveAuditChecklistAsync(
        int appId, IReadOnlyList<AuditMark> marks, string auditorUserId, CancellationToken ct);

    Task<AuditActionResult> ApproveForAgreementAsync(int appId, string auditorUserId, CancellationToken ct);

    Task<AuditActionResult> ConfirmPdfAsync(int appId, string auditorUserId, CancellationToken ct);

    Task<AuditActionResult> ReleaseForSignatureAsync(int appId, string auditorUserId, CancellationToken ct);

    Task<AuditActionResult> ReturnToReviewerAsync(int appId, string auditorUserId, CancellationToken ct);

    /// <summary>
    /// Spec 040 — true when the recorded auditor-stage responses cover every active
    /// required item as compliant (no non-compliant marks). Feeds the controller's
    /// PDF-generation re-gate (<c>CanAuditorGenerateFundingAgreement</c>).
    /// </summary>
    Task<bool> IsAuditChecklistCompleteAsync(int appId, CancellationToken ct);
}
