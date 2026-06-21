using FundingPlatform.Application.Audit;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 040 / T025 — orchestrates the auditor workflow stage. Mirrors the
/// <c>ReviewService</c> two-phase save (workflow first so the <c>VersionHistory</c> row
/// carries its id; outbox second). Checklist responses are snapshot rows in
/// <c>dbo.ApplicationChecklistResponses</c> (current row per app+stage+item, overwritten
/// each cycle); the transition-level audit trail lives in <c>VersionHistory</c>.
/// </summary>
public sealed class AuditWorkflowService : IAuditWorkflowService
{
    private readonly IApplicationRepository _applications;
    private readonly IChecklistTemplateRepository _checklists;
    private readonly INotificationOutboxWriter _outbox;
    private readonly AppDbContext _db;
    private readonly ILogger<AuditWorkflowService> _logger;

    public AuditWorkflowService(
        IApplicationRepository applications,
        IChecklistTemplateRepository checklists,
        INotificationOutboxWriter outbox,
        AppDbContext db,
        ILogger<AuditWorkflowService> logger)
    {
        _applications = applications;
        _checklists = checklists;
        _outbox = outbox;
        _db = db;
        _logger = logger;
    }

    // ----- reviewer side -----

    public Task<AuditActionResult> SubmitReviewerChecklistAndSendToAuditAsync(
        int appId, IReadOnlyList<ReviewerCheck> checks, string reviewerUserId, CancellationToken ct)
        => SendReviewerToAuditAsync(appId, checks, reviewerUserId, isResend: false, ct);

    public Task<AuditActionResult> ResendToAuditAsync(
        int appId, IReadOnlyList<ReviewerCheck> checks, string reviewerUserId, CancellationToken ct)
        => SendReviewerToAuditAsync(appId, checks, reviewerUserId, isResend: true, ct);

    private async Task<AuditActionResult> SendReviewerToAuditAsync(
        int appId, IReadOnlyList<ReviewerCheck> checks, string reviewerUserId, bool isResend, CancellationToken ct)
    {
        var application = await _applications.GetByIdWithResponseAndAppealsAsync(appId);
        if (application is null) return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));

        try
        {
            var (complete, rows) = await BuildReviewerResponsesAsync(appId, checks, reviewerUserId, ct);

            var vh = isResend
                ? application.ResendToAudit(reviewerUserId, complete)
                : application.SendToAudit(reviewerUserId, complete);

            await ReplaceStageResponsesAsync(appId, ChecklistStage.Reviewer, rows, ct);
            // Spec 040 — each audit cycle starts from a clean auditor checklist: re-sending
            // clears the prior cycle's auditor responses (stale findings/non-compliance) so
            // the gate evaluates only the current cycle.
            if (isResend)
            {
                await ReplaceStageResponsesAsync(appId, ChecklistStage.Auditor,
                    Array.Empty<ApplicationChecklistResponse>(), ct);
            }
            await _applications.UpdateAsync(application);
            await _applications.SaveChangesAsync();

            _logger.LogInformation(
                "Application {AppId} {Transition} to audit by {Actor}.",
                appId, isResend ? "re-sent" : "sent", reviewerUserId);

            await EnqueueAfterCommitAsync(
                () => EnqueueSentToAuditAsync(application, vh.Id, reviewerUserId, ct),
                appId, "SentToAuditAuditor");
            return AuditActionResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return AuditActionResult.Fail(
                UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification), conflict: true);
        }
    }

    public async Task<ReviewerChecklistView> GetReviewerChecklistAsync(int appId, CancellationToken ct)
    {
        var template = await _checklists.GetActiveForStageAsync(ChecklistStage.Reviewer, ct);
        var activeItems = template?.Items.Where(i => i.IsActive).OrderBy(i => i.DisplayOrder).ToList()
                          ?? new List<ChecklistTemplateItem>();

        var recorded = await _db.ApplicationChecklistResponses
            .AsNoTracking()
            .Where(r => r.ApplicationId == appId && r.Stage == ChecklistStage.Reviewer)
            .ToListAsync(ct);
        var checkedIds = recorded.Select(r => r.ChecklistTemplateItemId).ToHashSet();

        var items = activeItems
            .Select(i => new ReviewerChecklistItemView(i.Id, i.Text, i.IsRequired, checkedIds.Contains(i.Id)))
            .ToList();

        // Auditor non-compliance findings (surfaced to the reviewer when returned from audit).
        var findings = await _db.ApplicationChecklistResponses
            .AsNoTracking()
            .Where(r => r.ApplicationId == appId
                && r.Stage == ChecklistStage.Auditor
                && r.Status == ChecklistResponseStatus.NotCompliant)
            .Select(r => new AuditFindingView(r.ItemTextSnapshot, r.NonComplianceReason))
            .ToListAsync(ct);

        return new ReviewerChecklistView(items, findings);
    }

    // ----- auditor side -----

    public async Task<AuditChecklistView?> GetAuditChecklistAsync(int appId, CancellationToken ct)
    {
        var application = await _applications.GetByIdWithResponseAndAppealsAsync(appId);
        if (application is null) return null;

        var template = await _checklists.GetActiveForStageAsync(ChecklistStage.Auditor, ct);
        var activeItems = template?.Items.Where(i => i.IsActive).OrderBy(i => i.DisplayOrder).ToList()
                          ?? new List<ChecklistTemplateItem>();

        var recorded = await _db.ApplicationChecklistResponses
            .AsNoTracking()
            .Where(r => r.ApplicationId == appId && r.Stage == ChecklistStage.Auditor)
            .ToListAsync(ct);
        var byItem = recorded.ToDictionary(r => r.ChecklistTemplateItemId);

        var items = activeItems.Select(i =>
        {
            byItem.TryGetValue(i.Id, out var r);
            return new AuditChecklistItemView(i.Id, i.Text, i.IsRequired, r?.Status, r?.NonComplianceReason);
        }).ToList();

        var requiredIds = activeItems.Where(i => i.IsRequired).Select(i => i.Id).ToHashSet();
        var allRequiredCompliant = requiredIds.All(id => byItem.TryGetValue(id, out var r)
            && r.Status == ChecklistResponseStatus.Checked);
        var hasNonCompliant = recorded.Any(r => r.Status == ChecklistResponseStatus.NotCompliant);

        var agreement = application.FundingAgreement;
        return new AuditChecklistView(
            appId,
            template?.Id,
            ChecklistStage.Auditor,
            items,
            allRequiredCompliant,
            hasNonCompliant,
            AgreementExists: agreement is not null,
            AgreementConfirmed: agreement?.AuditorConfirmedAtUtc is not null);
    }

    public async Task<AuditActionResult> SaveAuditChecklistAsync(
        int appId, IReadOnlyList<AuditMark> marks, string auditorUserId, CancellationToken ct)
    {
        var application = await _applications.GetByIdWithResponseAndAppealsAsync(appId);
        if (application is null) return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));
        if (application.State != ApplicationState.PendingAudit)
            return AuditActionResult.Fail(UserFacingError.From(
                UserFacingErrorCode.OperationRejected, "La solicitud no está en auditoría."));

        var build = await BuildAuditorResponsesAsync(appId, marks, auditorUserId, ct);
        if (build.Error is not null) return AuditActionResult.Fail(build.Error);

        try
        {
            await ReplaceStageResponsesAsync(appId, ChecklistStage.Auditor, build.Rows, ct);
            await _db.SaveChangesAsync(ct);
            return AuditActionResult.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return AuditActionResult.Fail(
                UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification), conflict: true);
        }
        catch (DbUpdateException ex)
        {
            // Spec 040 — the UX_ApplicationChecklistResponses unique index turns a concurrent
            // duplicate-insert race (two auditors saving the same application) into a clean
            // stale-state refusal instead of duplicate rows that would later break reads.
            _logger.LogWarning(ex, "Checklist save conflict for application {AppId}.", appId);
            return AuditActionResult.Fail(
                UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification), conflict: true);
        }
    }

    public async Task<AuditActionResult> ApproveForAgreementAsync(int appId, string auditorUserId, CancellationToken ct)
    {
        var application = await _applications.GetByIdWithResponseAndAppealsAsync(appId);
        if (application is null) return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));

        var (allRequiredCompliant, hasNonCompliant) = await EvaluateRecordedAuditAsync(appId, ct);
        if (!allRequiredCompliant || hasNonCompliant)
            return AuditActionResult.Fail(UserFacingError.From(
                UserFacingErrorCode.OperationRejected,
                "No se puede aprobar: hay ítems de auditoría sin marcar como conformes."));

        if (application.State != ApplicationState.PendingAudit)
            return AuditActionResult.Fail(UserFacingError.From(
                UserFacingErrorCode.OperationRejected, "La solicitud no está en auditoría."));

        application.AddVersionHistory(new VersionHistory(auditorUserId, "AuditApproved", "Auditoría aprobada"));
        await _applications.UpdateAsync(application);
        await _applications.SaveChangesAsync();
        return AuditActionResult.Ok();
    }

    public async Task<AuditActionResult> ConfirmPdfAsync(int appId, string auditorUserId, CancellationToken ct)
    {
        var application = await _applications.GetByIdWithResponseAndAppealsAsync(appId);
        if (application is null) return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));

        try
        {
            application.ConfirmAgreementPdf(auditorUserId);
            await _applications.UpdateAsync(application);
            await _applications.SaveChangesAsync();
            return AuditActionResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
    }

    public async Task<AuditActionResult> ReleaseForSignatureAsync(int appId, string auditorUserId, CancellationToken ct)
    {
        var application = await _applications.GetByIdWithResponseAndAppealsAsync(appId);
        if (application is null) return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));

        try
        {
            var vh = application.ReleaseForSignature(auditorUserId);
            await _applications.UpdateAsync(application);
            await _applications.SaveChangesAsync();
            _logger.LogInformation("Application {AppId} released for signature by {Actor}.", appId, auditorUserId);

            // Spec 040 / D10 — re-pointed "ready to sign" notification fires on release.
            await EnqueueAfterCommitAsync(
                () => EnqueueAgreementReadyAsync(application, vh.Id, auditorUserId, ct),
                appId, "AgreementGeneratedApplicant");
            return AuditActionResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return AuditActionResult.Fail(
                UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification), conflict: true);
        }
    }

    public async Task<AuditActionResult> ReturnToReviewerAsync(int appId, string auditorUserId, CancellationToken ct)
    {
        var application = await _applications.GetByIdWithResponseAndAppealsAsync(appId);
        if (application is null) return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));

        var (_, hasNonCompliant) = await EvaluateRecordedAuditAsync(appId, ct);
        if (!hasNonCompliant)
            return AuditActionResult.Fail(UserFacingError.From(
                UserFacingErrorCode.OperationRejected,
                "Para devolver al revisor, marque al menos un ítem como no conforme con su motivo."));

        try
        {
            var vh = application.ReturnFromAudit(auditorUserId);
            await _applications.UpdateAsync(application);
            await _applications.SaveChangesAsync();
            _logger.LogInformation("Application {AppId} returned to reviewer by {Actor}.", appId, auditorUserId);

            await EnqueueAfterCommitAsync(
                () => EnqueueReturnedToReviewerAsync(application, vh.Id, auditorUserId, ct),
                appId, "ReturnedToReviewerFromAudit");
            return AuditActionResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return AuditActionResult.Fail(UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return AuditActionResult.Fail(
                UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification), conflict: true);
        }
    }

    // ----- helpers -----

    private async Task<(bool Complete, List<ApplicationChecklistResponse> Rows)> BuildReviewerResponsesAsync(
        int appId, IReadOnlyList<ReviewerCheck> checks, string userId, CancellationToken ct)
    {
        var template = await _checklists.GetActiveForStageAsync(ChecklistStage.Reviewer, ct);
        if (template is null) return (true, new List<ApplicationChecklistResponse>());

        var activeItems = template.Items.Where(i => i.IsActive).ToList();
        var requiredIds = activeItems.Where(i => i.IsRequired).Select(i => i.Id).ToHashSet();
        var checkedIds = checks.Where(c => c.Checked).Select(c => c.TemplateItemId).ToHashSet();
        var complete = requiredIds.All(checkedIds.Contains);

        var rows = activeItems
            .Where(i => checkedIds.Contains(i.Id))
            .Select(i => new ApplicationChecklistResponse(
                appId, ChecklistStage.Reviewer, i.Id, i.Text,
                ChecklistResponseStatus.Checked, null, userId))
            .ToList();
        return (complete, rows);
    }

    private async Task<(bool AllRequiredCompliant, bool HasNonCompliant, List<ApplicationChecklistResponse> Rows, UserFacingError? Error)>
        BuildAuditorResponsesAsync(int appId, IReadOnlyList<AuditMark> marks, string userId, CancellationToken ct)
    {
        var template = await _checklists.GetActiveForStageAsync(ChecklistStage.Auditor, ct);
        var activeItems = template?.Items.Where(i => i.IsActive).ToList() ?? new List<ChecklistTemplateItem>();
        var markById = marks.GroupBy(m => m.TemplateItemId).ToDictionary(g => g.Key, g => g.Last());

        var rows = new List<ApplicationChecklistResponse>();
        foreach (var item in activeItems)
        {
            if (!markById.TryGetValue(item.Id, out var mark)) continue;
            if (!mark.Compliant && string.IsNullOrWhiteSpace(mark.Reason))
            {
                return (false, false, new List<ApplicationChecklistResponse>(),
                    UserFacingError.From(UserFacingErrorCode.OperationRejected,
                        "Cada ítem no conforme requiere un motivo."));
            }

            rows.Add(new ApplicationChecklistResponse(
                appId, ChecklistStage.Auditor, item.Id, item.Text,
                mark.Compliant ? ChecklistResponseStatus.Checked : ChecklistResponseStatus.NotCompliant,
                mark.Compliant ? null : mark.Reason, userId));
        }

        var requiredIds = activeItems.Where(i => i.IsRequired).Select(i => i.Id).ToHashSet();
        var rowByItem = rows.ToDictionary(r => r.ChecklistTemplateItemId);
        var allRequiredCompliant = requiredIds.All(id => rowByItem.TryGetValue(id, out var r)
            && r.Status == ChecklistResponseStatus.Checked);
        var hasNonCompliant = rows.Any(r => r.Status == ChecklistResponseStatus.NotCompliant);
        return (allRequiredCompliant, hasNonCompliant, rows, null);
    }

    private async Task<(bool AllRequiredCompliant, bool HasNonCompliant)> EvaluateRecordedAuditAsync(
        int appId, CancellationToken ct)
    {
        var template = await _checklists.GetActiveForStageAsync(ChecklistStage.Auditor, ct);
        var requiredIds = template?.Items.Where(i => i.IsActive && i.IsRequired).Select(i => i.Id).ToHashSet()
                          ?? new HashSet<int>();
        var responses = await _db.ApplicationChecklistResponses
            .AsNoTracking()
            .Where(r => r.ApplicationId == appId && r.Stage == ChecklistStage.Auditor)
            .ToListAsync(ct);
        var byItem = responses.ToDictionary(r => r.ChecklistTemplateItemId);

        var allRequiredCompliant = requiredIds.All(id => byItem.TryGetValue(id, out var r)
            && r.Status == ChecklistResponseStatus.Checked);
        var hasNonCompliant = responses.Any(r => r.Status == ChecklistResponseStatus.NotCompliant);
        return (allRequiredCompliant, hasNonCompliant);
    }

    /// <summary>
    /// Spec 040 — the auditor generation gate, evaluated against recorded auditor-stage
    /// responses (all active required items Checked). Surfaced for the controller re-gate.
    /// </summary>
    public async Task<bool> IsAuditChecklistCompleteAsync(int appId, CancellationToken ct)
    {
        var (allRequiredCompliant, hasNonCompliant) = await EvaluateRecordedAuditAsync(appId, ct);
        return allRequiredCompliant && !hasNonCompliant;
    }

    private async Task ReplaceStageResponsesAsync(
        int appId, ChecklistStage stage, IEnumerable<ApplicationChecklistResponse> rows, CancellationToken ct)
    {
        var existing = await _db.ApplicationChecklistResponses
            .Where(r => r.ApplicationId == appId && r.Stage == stage)
            .ToListAsync(ct);
        _db.ApplicationChecklistResponses.RemoveRange(existing);
        await _db.ApplicationChecklistResponses.AddRangeAsync(rows, ct);
    }

    /// <summary>
    /// Spec 040 / FR-011 (scenario 5) — runs the phase-2 outbox enqueue + commit AFTER the
    /// workflow transition has already committed. A notification failure here MUST NOT roll back
    /// (or fail) the transition: it is logged and swallowed so the state change stands and an
    /// operator can re-drive the email. Mirrors the existing outbox-resilience posture.
    /// </summary>
    private async Task EnqueueAfterCommitAsync(Func<Task> enqueue, int appId, string eventName)
    {
        try
        {
            await enqueue();
            await _applications.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Notification enqueue failed for {Event} on application {AppId}; the workflow "
                + "transition already committed. The email was not sent and must be re-driven.",
                eventName, appId);
        }
    }

    private async Task EnqueueSentToAuditAsync(AppEntity application, int versionHistoryId, string actorUserId, CancellationToken ct)
    {
        var payload = BuildPayload(application, actorUserId,
            await _outbox.GetApplicantStageGroupIdsAsync(application.Id, ct));
        await _outbox.EnqueueAsync(NotificationEvent.SentToAuditAuditor, application.Id, versionHistoryId, payload, ct);
    }

    private async Task EnqueueReturnedToReviewerAsync(AppEntity application, int versionHistoryId, string actorUserId, CancellationToken ct)
    {
        // Spec 040 / FR-011 — include the auditor's per-item non-compliance findings in the
        // email body ("item — reason") so the reviewer sees the reasons directly.
        var findings = await _db.ApplicationChecklistResponses
            .AsNoTracking()
            .Where(r => r.ApplicationId == application.Id
                && r.Stage == ChecklistStage.Auditor
                && r.Status == ChecklistResponseStatus.NotCompliant)
            .Select(r => r.ItemTextSnapshot + (r.NonComplianceReason == null ? "" : " — " + r.NonComplianceReason))
            .ToListAsync(ct);

        var payload = BuildPayload(application, actorUserId,
            await _outbox.GetApplicantStageGroupIdsAsync(application.Id, ct)) with { AuditFindings = findings };
        await _outbox.EnqueueAsync(NotificationEvent.ReturnedToReviewerFromAudit, application.Id, versionHistoryId, payload, ct);
    }

    private async Task EnqueueAgreementReadyAsync(AppEntity application, int versionHistoryId, string actorUserId, CancellationToken ct)
    {
        // Re-pointed AgreementGeneratedApplicant — applicant bucket (no stage groups needed).
        var payload = BuildPayload(application, actorUserId, Array.Empty<int>());
        await _outbox.EnqueueAsync(NotificationEvent.AgreementGeneratedApplicant, application.Id, versionHistoryId, payload, ct);
    }

    private static NotificationPayload BuildPayload(AppEntity application, string actorUserId, IReadOnlyList<int> stageGroupIds)
    {
        var applicantDisplayName = application.Applicant is not null
            ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
            : "Solicitante";
        var applicantUserId = application.Applicant?.UserId ?? string.Empty;
        return new NotificationPayload(
            application.Id, applicantUserId, applicantDisplayName, stageGroupIds, OutcomeCode: null, ActorUserId: actorUserId);
    }
}
