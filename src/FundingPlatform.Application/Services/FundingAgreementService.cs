using System.Globalization;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.FundingAgreements.Commands;
using FundingPlatform.Application.FundingAgreements.Queries;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.Notifications;
using Microsoft.Extensions.Logging;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

public record GenerateFundingAgreementResult(
    bool Success,
    FundingAgreementDto? Agreement,
    IReadOnlyList<UserFacingError> Errors,
    bool ConflictDetected);

public record GetPanelResult(
    bool Authorized,
    FundingAgreementPanelDto? Panel);

public class FundingAgreementService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<FundingAgreementService> _logger;
    // Spec 027 / US1 — resolve the generator's display name (never a GUID).
    private readonly IUserStoreReader _userStoreReader;
    // Spec 028 / US3 — convenio-generated notification via the spec-021 outbox.
    private readonly INotificationOutboxWriter _outboxWriter;

    public FundingAgreementService(
        IApplicationRepository applicationRepository,
        ILogger<FundingAgreementService> logger,
        IUserStoreReader userStoreReader,
        INotificationOutboxWriter outboxWriter)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
        _userStoreReader = userStoreReader;
        _outboxWriter = outboxWriter;
    }

    public async Task<GetPanelResult> GetPanelAsync(GetFundingAgreementPanelQuery query)
    {
        var application = await _applicationRepository.GetByIdWithResponseAndAppealsAsync(query.ApplicationId);
        if (application is null) return new GetPanelResult(false, null);

        var canAccess = application.CanUserAccessFundingAgreement(
            applicantUserId: query.UserId,
            isAdministrator: query.IsAdministrator,
            isReviewerAssignedToThisApplication: query.IsReviewerAssigned);

        if (!canAccess) return new GetPanelResult(false, null);

        var canUserGenerate = application.CanUserGenerateFundingAgreement(
            isAdministrator: query.IsAdministrator,
            isReviewerAssignedToThisApplication: query.IsReviewerAssigned);

        var preconditionsOk = application.CanGenerateFundingAgreement(out var errors);
        var disabledReason = preconditionsOk ? null : errors.FirstOrDefault();

        var agreement = application.FundingAgreement;
        var agreementExists = agreement is not null;

        var canGenerate = canUserGenerate && preconditionsOk && !agreementExists;
        var canRegenerate = canUserGenerate && preconditionsOk && agreementExists;

        // Spec 027 / US1 — resolve the generator to a human display name; never
        // surface the raw GeneratedByUserId (GUID) on the page (FR-001/FR-002).
        string? generatedByDisplayName = null;
        if (agreement?.GeneratedByUserId is { Length: > 0 } generatorId)
        {
            var resolved = await _userStoreReader.GetDisplayNameAsync(generatorId, CancellationToken.None);
            generatedByDisplayName = GeneratorDisplayName.FromResolved(resolved, generatorId);
        }

        var panel = new FundingAgreementPanelDto(
            ApplicationId: application.Id,
            AgreementExists: agreementExists,
            CanGenerate: canGenerate,
            CanRegenerate: canRegenerate,
            DisabledReason: agreementExists ? null : disabledReason,
            GeneratedAtUtc: agreement?.GeneratedAtUtc,
            GeneratedByUserId: agreement?.GeneratedByUserId,
            GeneratedByDisplayName: generatedByDisplayName);

        return new GetPanelResult(true, panel);
    }

    public async Task<AppEntity?> LoadForGenerationAsync(int applicationId)
    {
        return await _applicationRepository.GetByIdWithResponseAndAppealsAsync(applicationId);
    }

    // Spec 028 / US3 — applicant-bucket enqueue for AGREEMENT_GENERATED_APPLICANT.
    // Two-phase: called AFTER the workflow SaveChangesAsync so the VersionHistory
    // row carries its identity (the idempotency anchor).
    private async Task EnqueueAgreementGeneratedAsync(
        AppEntity application, int versionHistoryId, string actorUserId)
    {
        var applicantDisplayName = application.Applicant is not null
            ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
            : "Solicitante";
        var applicantUserId = application.Applicant?.UserId ?? string.Empty;
        var payload = new NotificationPayload(
            application.Id, applicantUserId, applicantDisplayName,
            Array.Empty<int>(), OutcomeCode: null, ActorUserId: actorUserId);
        await _outboxWriter.EnqueueAsync(
            NotificationEvent.AgreementGeneratedApplicant, application.Id, versionHistoryId,
            payload, CancellationToken.None);
        await _applicationRepository.SaveChangesAsync();
    }

    public async Task<GenerateFundingAgreementResult> PersistGenerationAsync(
        AppEntity application,
        string userId,
        string fileName,
        long size,
        string blobKey)
    {
        try
        {
            FundingAgreement agreement;
            if (application.FundingAgreement is null)
            {
                agreement = application.GenerateFundingAgreement(
                    fileName, "application/pdf", size, blobKey, userId);
            }
            else
            {
                agreement = application.RegenerateFundingAgreement(
                    fileName, "application/pdf", size, blobKey, userId);
            }

            // Spec 028 / US3 / FR-010 / R-007 — convenio generation previously wrote
            // no audit row. Add one (via the domain method, §II) so the idempotency
            // anchor is uniform across all 12 events and generation is auditable.
            // Regeneration writes a fresh row → fresh VersionHistoryId → fresh email.
            var vhRow = new VersionHistory(
                userId,
                "AgreementGenerated",
                $"Convenio generado (versión {agreement.GeneratedVersion})");
            application.AddVersionHistory(vhRow);

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            // Spec 028 / US3 / FR-010 — notify the applicant the convenio is ready
            // to sign (actor = the generating reviewer/admin, excluded if also an
            // admin recipient). Two-phase save mirrors ReviewService.SendBackAsync.
            await EnqueueAgreementGeneratedAsync(application, vhRow.Id, actorUserId: userId);

            var dto = new FundingAgreementDto(
                application.Id,
                agreement.FileName,
                agreement.ContentType,
                agreement.Size,
                agreement.GeneratedAtUtc,
                agreement.GeneratedByUserId);

            _logger.LogInformation(
                "Funding agreement generated. applicationId={ApplicationId} actingUserId={UserId} fileSize={FileSize}",
                application.Id, userId, agreement.Size);

            return new GenerateFundingAgreementResult(true, dto, Array.Empty<UserFacingError>(), false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Funding agreement generation rejected. applicationId={ApplicationId} actingUserId={UserId} failureReason={Reason}",
                application.Id, userId, ex.Message);
            return new GenerateFundingAgreementResult(false, null,
                new[] { UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message) },
                false);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            _logger.LogWarning(ex,
                "Funding agreement generation concurrency conflict. applicationId={ApplicationId}",
                application.Id);
            return new GenerateFundingAgreementResult(false, null,
                new[] { UserFacingError.From(UserFacingErrorCode.ConcurrentAgreementModification) },
                true);
        }
    }
}
