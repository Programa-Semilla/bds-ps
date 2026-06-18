using FundingPlatform.Application.Errors;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Audit;

/// <summary>Spec 040 / D7 — one row in the group-scoped auditor inbox of PendingAudit apps.</summary>
public sealed record AuditInboxRowDto(
    int ApplicationId,
    string ApplicantDisplayName,
    string? PublicCode,
    DateTime EnteredAuditAtUtc,
    int ItemCount,
    bool HasProviderWarning);

/// <summary>
/// Spec 040 — a reviewer's tick of a single reviewer-stage checklist item
/// (send-to-audit / re-send). Reviewers only ever check items (never mark non-compliant).
/// </summary>
public sealed record ReviewerCheck(int TemplateItemId, bool Checked);

/// <summary>
/// Spec 040 — an auditor's mark on a single auditor-stage checklist item. A non-compliant
/// mark (<c>Compliant == false</c>) requires a non-empty <see cref="Reason"/>.
/// </summary>
public sealed record AuditMark(int TemplateItemId, bool Compliant, string? Reason);

/// <summary>Spec 040 — a single checklist line rendered to the reviewer/auditor, with any prior recorded mark.</summary>
public sealed record AuditChecklistItemView(
    int TemplateItemId,
    string Text,
    bool IsRequired,
    ChecklistResponseStatus? RecordedStatus,
    string? RecordedReason);

/// <summary>
/// Spec 040 — the auditor's checklist render plus the audit-stage workflow flags the
/// detail view needs to decide which buttons to show.
/// </summary>
public sealed record AuditChecklistView(
    int ApplicationId,
    int? TemplateId,
    ChecklistStage Stage,
    IReadOnlyList<AuditChecklistItemView> Items,
    bool AllRequiredCompliant,
    bool HasAnyNonCompliant,
    bool AgreementExists,
    bool AgreementConfirmed);

/// <summary>
/// Spec 040 — the result of an audit-workflow mutation. On failure carries a
/// <see cref="UserFacingError"/> already localized to es-CR at the Web boundary.
/// </summary>
public sealed record AuditActionResult(
    bool Success,
    UserFacingError? Error = null,
    bool ConflictDetected = false)
{
    public static AuditActionResult Ok() => new(true);
    public static AuditActionResult Fail(UserFacingError error, bool conflict = false) => new(false, error, conflict);
}
