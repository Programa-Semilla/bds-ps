using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.FundingAgreements.Commands;
using FundingPlatform.Application.FundingAgreements.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
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

    public FundingAgreementService(
        IApplicationRepository applicationRepository,
        ILogger<FundingAgreementService> logger)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
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

        var panel = new FundingAgreementPanelDto(
            ApplicationId: application.Id,
            AgreementExists: agreementExists,
            CanGenerate: canGenerate,
            CanRegenerate: canRegenerate,
            DisabledReason: agreementExists ? null : disabledReason,
            GeneratedAtUtc: agreement?.GeneratedAtUtc,
            GeneratedByUserId: agreement?.GeneratedByUserId,
            GeneratedByDisplayName: agreement?.GeneratedByUserId);

        return new GetPanelResult(true, panel);
    }

    public async Task<AppEntity?> LoadForGenerationAsync(int applicationId)
    {
        return await _applicationRepository.GetByIdWithResponseAndAppealsAsync(applicationId);
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

            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

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
