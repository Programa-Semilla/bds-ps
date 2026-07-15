// Spec 021 — see specs/021-feedback-session-may13/tasks.md T091.

using FundingPlatform.Application.Applications;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / T091 — EF-backed <see cref="ISubmitApplicationHandler"/>.
///
/// Spec 044 — the legacy Solicitud duration gate was removed; reception windows
/// (<c>ProcessEvents</c>) now gate submission timing. US2 (T026) layers the
/// reception-window evaluation here before <see cref="AppEntity.Submit(int)"/>.
/// </summary>
public sealed class SubmitApplicationHandler : ISubmitApplicationHandler
{
    private readonly AppDbContext _db;
    private readonly IStageExpiryClock _clock;
    private readonly INotificationOutboxWriter _outbox;
    private readonly Application.Processes.ReceptionWindows.IReceptionWindowQuery _receptionWindows;

    public SubmitApplicationHandler(
        AppDbContext db,
        IStageExpiryClock clock,
        INotificationOutboxWriter outbox,
        Application.Processes.ReceptionWindows.IReceptionWindowQuery receptionWindows)
    {
        _db = db;
        _clock = clock;
        _outbox = outbox;
        _receptionWindows = receptionWindows;
    }

    public async Task SubmitAsync(SubmitApplicationCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var application = await _db.Applications
            .Include(a => a.Items)
                .ThenInclude(i => i.Quotations)
            // Spec 035 / D11 — the submit gate (Application.Validate) checks each
            // item's required category fields against the category's CURRENT field
            // set, so Category.Fields + the item's stored values must be loaded.
            .Include(a => a.Items)
                .ThenInclude(i => i.Category)
                    .ThenInclude(c => c.Fields)
            .Include(a => a.Items)
                .ThenInclude(i => i.CategoryFieldValues)
            .Include(a => a.Applicant)
            .FirstOrDefaultAsync(a => a.Id == cmd.ApplicationId, ct)
            ?? throw new InvalidOperationException(
                $"Application {cmd.ApplicationId} not found.");

        // Spec 037 / FR-020 — the selected company must still be active at submit.
        // The draft re-select dropdown excludes archived companies, so the applicant
        // simply re-picks. (Pre-037 rows with no CompanyId are unaffected.) Surfaced
        // via the controller's InvalidOperationException → validation-errors path.
        if (application.CompanyId is int companyId)
        {
            var companyIsArchived = await _db.Companies
                .AnyAsync(c => c.Id == companyId && c.ArchivedAt != null, ct);
            if (companyIsArchived)
            {
                throw new InvalidOperationException(
                    "La empresa seleccionada fue archivada. Seleccione una empresa activa para enviar.");
            }
        }

        // Spec 044 / FR-008 — gate submission on an active reception window
        // (Application → Group → Process). No windows ⇒ Unrestricted ⇒ allowed
        // (FR-007). The refusal carries the relevant boundary instant for the
        // typed es-CR message (DomainExceptionFilter → 422). Point-in-time at
        // submit, so later window edits never affect a submitted application (FR-017).
        var availability = await _receptionWindows.GetAvailabilityForApplicationAsync(
            application.Id, _clock.UtcNow, ct);
        if (!availability.CanSubmit)
        {
            var boundary = availability.NextWindow?.StartUtc ?? availability.LastClosedWindow?.EndUtc;
            throw new FundingPlatform.Domain.Exceptions.ReceptionWindowClosedException(
                availability.Status, boundary);
        }

        var minQuotations = await ResolveMinimumQuotationsAsync(application, ct);

        // Spec 013 / FR-024 — every owned Draft supplier referenced by a
        // quotation flips to PendingReview atomically with the submission.
        // This carried over from ApplicationService.SubmitApplicationAsync
        // (the pre-spec-021 submit path); the spec-021 stage-aware handler
        // must preserve it or admin supplier verification has nothing to act
        // on after an applicant submits.
        var referencedSupplierIds = application.Items
            .SelectMany(i => i.Quotations)
            .Select(q => q.SupplierId)
            .Distinct()
            .ToList();
        if (referencedSupplierIds.Count > 0)
        {
            var suppliers = await _db.Suppliers
                .Where(s => referencedSupplierIds.Contains(s.Id))
                .ToListAsync(ct);
            foreach (var supplier in suppliers)
            {
                if (supplier.VerificationStatus == SupplierVerificationStatus.Draft
                    && supplier.CreatedByApplicantId == application.ApplicantId)
                {
                    supplier.SubmitForReview();
                }
            }
        }

        // Spec 021-email-notifications / FR-007 / R-003 — resubmit detection
        // must read BEFORE the new "Submitted" VersionHistory row is added so
        // the predicate reflects prior cycles only.
        var isResubmit = await _outbox.HasPriorSendBackAsync(application.Id, ct);

        application.Submit(minQuotations);

        // Spec 013 — workflow audit: a "Submitted" VersionHistory row marks the
        // Draft→Submitted transition. Pre-spec-021 ApplicationService.Submit
        // added this; the spec-021 stage-aware handler must keep it (the
        // reviewer queue + the notification idempotency key both depend on it).
        var actorUserId = application.Applicant?.UserId ?? string.Empty;
        var vhRow = new VersionHistory(actorUserId, "Submitted", "Application submitted for review");
        application.AddVersionHistory(vhRow);

        // Phase 1 — persist the workflow state change + VersionHistory row.
        await _db.SaveChangesAsync(ct);

        // Spec 021-email-notifications / FR-001 — enqueue the outbox rows in a
        // second save (workflow first, outbox second). Worktree's spec-021
        // submit handler replaced ApplicationService.SubmitApplicationAsync;
        // the outbox enqueue main added there must be carried on this path or
        // no APPLICATION_SUBMITTED_* / RESUBMITTED_BY_APPLICANT mail fires.
        var stageGroupIds = await _outbox.GetApplicantStageGroupIdsAsync(application.Id, ct);
        var applicantDisplayName = application.Applicant is not null
            ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
            : "Solicitante";
        var payload = new NotificationPayload(
            ApplicationId: application.Id,
            ApplicantUserId: actorUserId,
            ApplicantDisplayName: applicantDisplayName,
            StageGroupIds: stageGroupIds,
            OutcomeCode: null);

        if (isResubmit)
        {
            await _outbox.EnqueueAsync(
                NotificationEvent.ResubmittedByApplicant,
                application.Id, vhRow.Id, payload, ct);
        }
        else
        {
            await _outbox.EnqueueAsync(
                NotificationEvent.ApplicationSubmittedApplicant,
                application.Id, vhRow.Id, payload, ct);
            await _outbox.EnqueueAsync(
                NotificationEvent.ApplicationSubmittedReviewer,
                application.Id, vhRow.Id, payload, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> ResolveMinimumQuotationsAsync(AppEntity application, CancellationToken ct)
    {
        // Spec 029 / FR-017 — resolve the Plantilla deterministically through the
        // application's Group anchor (Group → Process → ProcessPlantilla).
        var snapshot = await (
            from g in _db.Groups
            where g.Id == application.GroupId
            join pp in _db.ProcessPlantillas on g.ProcessId equals pp.ProcessId
            select pp).FirstOrDefaultAsync(ct);
        if (snapshot is not null)
        {
            return snapshot.MinimumQuotationsPerItem;
        }

        var config = await _db.SystemConfigurations
            .FirstOrDefaultAsync(c => c.Key == "MinQuotationsPerItem", ct);
        if (config is not null && int.TryParse(config.Value, out var parsed) && parsed > 0)
        {
            return parsed;
        }
        return 2;
    }

}
