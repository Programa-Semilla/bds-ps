using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Applications.Queries;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.Notifications;
using Microsoft.Extensions.Logging;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

public class ApplicantResponseService
{
    private const int DefaultMaxAppeals = 1;

    private readonly IApplicationRepository _applicationRepository;
    private readonly ISystemConfigurationRepository _systemConfigurationRepository;
    // Spec 028 — post-resolution notifications. Enqueued through the shipped
    // spec-021 transactional outbox using the canonical two-phase save.
    private readonly INotificationOutboxWriter _outboxWriter;
    private readonly ILogger<ApplicantResponseService> _logger;

    public ApplicantResponseService(
        IApplicationRepository applicationRepository,
        ISystemConfigurationRepository systemConfigurationRepository,
        INotificationOutboxWriter outboxWriter,
        ILogger<ApplicantResponseService> logger)
    {
        _applicationRepository = applicationRepository;
        _systemConfigurationRepository = systemConfigurationRepository;
        _outboxWriter = outboxWriter;
        _logger = logger;
    }

    public async Task<ApplicantResponseDto?> GetResponseAsync(GetApplicantResponseQuery query, int applicantId)
    {
        var application = await _applicationRepository.GetByIdWithResponseAndAppealsAsync(query.ApplicationId);
        if (application is null) return null;
        if (application.ApplicantId != applicantId) return null;

        return MapToResponseDto(application);
    }

    public async Task<(ApplicantResponseDto? Result, UserFacingError? Error)> SubmitResponseAsync(
        SubmitApplicantResponseCommand command,
        int applicantId)
    {
        var application = await _applicationRepository.GetByIdWithResponseAndAppealsAsync(command.ApplicationId);
        if (application is null)
            return (null, UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));
        if (application.ApplicantId != applicantId)
            return (null, UserFacingError.From(UserFacingErrorCode.ApplicationNotOwnedByApplicant));

        try
        {
            application.SubmitResponse(command.ItemDecisions, command.UserId);
            var vhRow = new VersionHistory(
                command.UserId,
                "SubmitResponse",
                $"Applicant response submitted (cycle {application.ApplicantResponses.Count})");
            application.AddVersionHistory(vhRow);

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            // Spec 028 / US1 / FR-001 — notify stage-group reviewers + admins that
            // the applicant responded (the actor is the applicant, excluded from
            // recipients). Two-phase save mirrors ReviewService.SendBackAsync.
            await EnqueueReviewerEventAsync(
                application, NotificationEvent.ResponseSubmittedReviewer,
                vhRow.Id, actorUserId: command.UserId);

            return (MapToResponseDto(application), null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return (null, UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification));
        }
    }

    public async Task<(AppealDto? Result, UserFacingError? Error)> OpenAppealAsync(
        OpenAppealCommand command,
        int applicantId)
    {
        var application = await _applicationRepository.GetByIdWithResponseAndAppealsAsync(command.ApplicationId);
        if (application is null)
            return (null, UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));
        if (application.ApplicantId != applicantId)
            return (null, UserFacingError.From(UserFacingErrorCode.ApplicationNotOwnedByApplicant));

        var maxAppeals = await GetMaxAppealsAsync();

        try
        {
            var appeal = application.OpenAppeal(command.UserId, maxAppeals);
            var vhRow = new VersionHistory(
                command.UserId,
                "OpenAppeal",
                "Applicant opened an appeal");
            application.AddVersionHistory(vhRow);

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            // Spec 028 / US2 / FR-002 — notify reviewers + admins (actor = applicant).
            await EnqueueReviewerEventAsync(
                application, NotificationEvent.AppealOpenedReviewer,
                vhRow.Id, actorUserId: command.UserId);

            return (MapAppealToDto(appeal, application), null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return (null, UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification));
        }
    }

    public async Task<AppealDto?> GetAppealAsync(GetAppealQuery query, int? applicantId)
    {
        var application = await _applicationRepository.GetByIdWithResponseAndAppealsAsync(query.ApplicationId);
        if (application is null) return null;

        if (!query.IsReviewer)
        {
            if (applicantId is null || application.ApplicantId != applicantId) return null;
        }

        var appeal = application.Appeals
            .OrderByDescending(a => a.OpenedAt)
            .FirstOrDefault();
        if (appeal is null) return null;

        return MapAppealToDto(appeal, application);
    }

    public async Task<(AppealDto? Result, UserFacingError? Error)> PostMessageAsync(
        PostAppealMessageCommand command,
        int? applicantId,
        bool isReviewer)
    {
        var application = await _applicationRepository.GetByIdWithResponseAndAppealsAsync(command.ApplicationId);
        if (application is null)
            return (null, UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));

        if (!isReviewer)
        {
            if (applicantId is null || application.ApplicantId != applicantId)
                return (null, UserFacingError.From(UserFacingErrorCode.AppealAccessDenied));
        }

        var appeal = application.Appeals
            .OrderByDescending(a => a.OpenedAt)
            .FirstOrDefault(a => a.Status == AppealStatus.Open);
        if (appeal is null)
            return (null, UserFacingError.From(UserFacingErrorCode.NoOpenAppealForMessage));

        try
        {
            appeal.PostMessage(command.UserId, command.Text);
            var vhRow = new VersionHistory(
                command.UserId,
                "PostAppealMessage",
                $"Message posted on appeal {appeal.Id}");
            application.AddVersionHistory(vhRow);

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            // Spec 028 / US2 / FR-003+FR-004 / R-002 — direction by author identity:
            // applicant authored → notify reviewers + admins; reviewer authored →
            // notify the applicant + admins. The author is the excluded actor.
            var authoredByApplicant = command.UserId == application.Applicant?.UserId;
            if (authoredByApplicant)
            {
                await EnqueueReviewerEventAsync(
                    application, NotificationEvent.AppealMessageReviewer,
                    vhRow.Id, actorUserId: command.UserId);
            }
            else
            {
                await EnqueueApplicantEventAsync(
                    application, NotificationEvent.AppealMessageApplicant,
                    vhRow.Id, actorUserId: command.UserId);
            }

            return (MapAppealToDto(appeal, application), null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return (null, UserFacingError.From(UserFacingErrorCode.ConcurrentAppealModification));
        }
    }

    public async Task<(AppealDto? Result, UserFacingError? Error)> ResolveAppealAsync(
        ResolveAppealCommand command)
    {
        var application = await _applicationRepository.GetByIdWithResponseAndAppealsAsync(command.ApplicationId);
        if (application is null)
            return (null, UserFacingError.From(UserFacingErrorCode.ApplicationNotFound));

        try
        {
            switch (command.Resolution)
            {
                case AppealResolution.Uphold:
                    application.ResolveAppealAsUphold(command.UserId);
                    break;
                case AppealResolution.GrantReopenToDraft:
                    application.ResolveAppealAsGrantReopenToDraft(command.UserId);
                    break;
                case AppealResolution.GrantReopenToReview:
                    application.ResolveAppealAsGrantReopenToReview(command.UserId);
                    break;
                default:
                    return (null, UserFacingError.From(
                        UserFacingErrorCode.UnknownAppealResolution, command.Resolution.ToString()));
            }

            var vhRow = new VersionHistory(
                command.UserId,
                "ResolveAppeal",
                $"Appeal resolved as {command.Resolution}");
            application.AddVersionHistory(vhRow);

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            // Spec 028 / US2 / FR-005+FR-006 — notify the applicant of the
            // resolution (body switches on OutcomeCode); on GrantReopenToReview
            // ALSO notify reviewers (dual-fire, same VersionHistoryId, distinct
            // EventType). Actor = the resolving reviewer.
            var outcomeCode = command.Resolution switch
            {
                AppealResolution.Uphold              => "AppealUpheld",
                AppealResolution.GrantReopenToDraft  => "AppealReopenedToDraft",
                AppealResolution.GrantReopenToReview => "AppealReopenedToReview",
                _ => null,
            };
            await EnqueueAppealResolvedAsync(
                application, vhRow.Id, actorUserId: command.UserId, outcomeCode: outcomeCode,
                alsoNotifyReviewers: command.Resolution == AppealResolution.GrantReopenToReview);

            var appeal = application.Appeals
                .OrderByDescending(a => a.ResolvedAt ?? a.OpenedAt)
                .First();
            return (MapAppealToDto(appeal, application), null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message));
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return (null, UserFacingError.From(UserFacingErrorCode.ConcurrentAppealModification));
        }
    }

    // -------------------------------------------------------------------------
    // Spec 028 — two-phase notification enqueue helpers. Called AFTER the workflow
    // SaveChangesAsync so the VersionHistory row has its identity (the idempotency
    // anchor); the second SaveChangesAsync commits the outbox row in the same UoW.
    // Mirrors the canonical pattern in ReviewService.SendBackAsync.
    // -------------------------------------------------------------------------

    private async Task EnqueueReviewerEventAsync(
        AppEntity application, NotificationEvent ev, int versionHistoryId, string actorUserId)
    {
        var stageGroupIds = await _outboxWriter.GetApplicantStageGroupIdsAsync(
            application.Id, CancellationToken.None);
        await EnqueueAsync(application, ev, versionHistoryId, actorUserId, stageGroupIds, outcomeCode: null);
    }

    private Task EnqueueApplicantEventAsync(
        AppEntity application, NotificationEvent ev, int versionHistoryId,
        string actorUserId, string? outcomeCode = null)
        // Applicant-bucket events do not use StageGroupIds (the reviewer query is skipped).
        => EnqueueAsync(application, ev, versionHistoryId, actorUserId, Array.Empty<int>(), outcomeCode);

    private async Task EnqueueAsync(
        AppEntity application, NotificationEvent ev, int versionHistoryId,
        string actorUserId, IReadOnlyList<int> stageGroupIds, string? outcomeCode)
    {
        var applicantDisplayName = application.Applicant is not null
            ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
            : "Solicitante";
        var applicantUserId = application.Applicant?.UserId ?? string.Empty;
        var payload = new NotificationPayload(
            application.Id, applicantUserId, applicantDisplayName,
            stageGroupIds, outcomeCode, ActorUserId: actorUserId);
        await _outboxWriter.EnqueueAsync(ev, application.Id, versionHistoryId, payload, CancellationToken.None);
        await _applicationRepository.SaveChangesAsync();
    }

    private async Task EnqueueAppealResolvedAsync(
        AppEntity application, int versionHistoryId, string actorUserId,
        string? outcomeCode, bool alsoNotifyReviewers)
    {
        var applicantDisplayName = application.Applicant is not null
            ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
            : "Solicitante";
        var applicantUserId = application.Applicant?.UserId ?? string.Empty;

        // Event 5 — applicant bucket; the partial switches body on OutcomeCode.
        var applicantPayload = new NotificationPayload(
            application.Id, applicantUserId, applicantDisplayName,
            Array.Empty<int>(), outcomeCode, ActorUserId: actorUserId);
        await _outboxWriter.EnqueueAsync(
            NotificationEvent.AppealResolvedApplicant, application.Id, versionHistoryId,
            applicantPayload, CancellationToken.None);

        // Event 6 — reviewer bucket, only on reopen-to-review (FR-006 dual-fire).
        if (alsoNotifyReviewers)
        {
            var stageGroupIds = await _outboxWriter.GetApplicantStageGroupIdsAsync(
                application.Id, CancellationToken.None);
            var reviewerPayload = new NotificationPayload(
                application.Id, applicantUserId, applicantDisplayName,
                stageGroupIds, OutcomeCode: null, ActorUserId: actorUserId);
            await _outboxWriter.EnqueueAsync(
                NotificationEvent.AppealReopenedReviewer, application.Id, versionHistoryId,
                reviewerPayload, CancellationToken.None);
        }

        // Single phase-2 save commits both rows atomically (the dual-fire shares
        // one VersionHistoryId; the unique index admits both via distinct EventType).
        await _applicationRepository.SaveChangesAsync();
    }

    private async Task<int> GetMaxAppealsAsync()
    {
        var config = await _systemConfigurationRepository.GetByKeyAsync("MaxAppealsPerApplication");
        if (config is null)
        {
            _logger.LogWarning("SystemConfiguration key 'MaxAppealsPerApplication' not found. Using default value of {Default}.", DefaultMaxAppeals);
            return DefaultMaxAppeals;
        }

        return int.TryParse(config.Value, out var parsed) ? parsed : DefaultMaxAppeals;
    }

    private static ApplicantResponseDto MapToResponseDto(AppEntity application)
    {
        var latestResponse = application.ApplicantResponses
            .OrderByDescending(r => r.CycleNumber)
            .FirstOrDefault();

        var decisionsByItemId = latestResponse is null
            ? new Dictionary<int, ItemResponseDecision>()
            : latestResponse.ItemResponses.ToDictionary(ir => ir.ItemId, ir => ir.Decision);

        var items = application.Items.Select(item =>
        {
            decimal? amount = null;
            string? supplierName = null;
            if (item.SelectedSupplierId is int supplierId)
            {
                var quotation = item.Quotations.FirstOrDefault(q => q.SupplierId == supplierId);
                amount = quotation?.Price;
                supplierName = quotation?.Supplier?.Name;
            }

            var hasDecision = decisionsByItemId.TryGetValue(item.Id, out var decision);

            return new ItemResponseDto(
                item.Id,
                item.ProductName,
                item.ReviewStatus,
                supplierName,
                amount,
                item.ReviewComment,
                hasDecision ? decision : null);
        }).ToList();

        return new ApplicantResponseDto(
            application.Id,
            latestResponse?.CycleNumber,
            latestResponse?.SubmittedAt,
            latestResponse is not null,
            application.State,
            items,
            application.FundingAgreement is not null);
    }

    private static AppealDto MapAppealToDto(Appeal appeal, AppEntity application)
    {
        var applicantUserId = application.Applicant?.UserId;
        var applicantDisplayName = application.Applicant is not null
            ? $"{application.Applicant.FirstName} {application.Applicant.LastName}"
            : "Applicant";

        var messages = appeal.Messages
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m =>
            {
                var isByApplicant = m.AuthorUserId == applicantUserId;
                return new AppealMessageDto(
                    m.Id,
                    m.AuthorUserId,
                    isByApplicant ? applicantDisplayName : "Reviewer",
                    isByApplicant,
                    m.Text,
                    m.CreatedAt);
            }).ToList();

        return new AppealDto(
            appeal.Id,
            appeal.ApplicationId,
            appeal.OpenedAt,
            appeal.OpenedByUserId,
            appeal.Status,
            appeal.Resolution,
            appeal.ResolvedAt,
            appeal.ResolvedByUserId,
            messages);
    }
}
