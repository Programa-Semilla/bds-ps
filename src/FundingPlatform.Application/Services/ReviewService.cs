using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

public class ReviewService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<ReviewService> _logger;

    private const int PageSize = 25;

    public ReviewService(IApplicationRepository applicationRepository, ILogger<ReviewService> logger)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<(List<ReviewQueueItemDto> Items, int TotalCount)> GetReviewQueueAsync(int page)
    {
        var (applications, totalCount) = await _applicationRepository.GetByStatePagedAsync(
            ApplicationState.Submitted, page, PageSize);

        var items = applications.Select(a => new ReviewQueueItemDto(
            a.Id,
            $"{a.Applicant.FirstName} {a.Applicant.LastName}",
            a.Applicant.PerformanceScore,
            a.SubmittedAt!.Value,
            a.Items.Count)).ToList();

        return (items, totalCount);
    }

    public async Task<(List<GenerateAgreementQueueRowDto> Items, int TotalCount)> GetGenerateAgreementQueueAsync(int page)
    {
        if (page < 1) page = 1;

        var (applications, totalCount) = await _applicationRepository.GetPendingAgreementPagedAsync(page, PageSize);

        var items = applications.Select(a => new GenerateAgreementQueueRowDto(
            a.Id,
            $"{a.Applicant.FirstName} {a.Applicant.LastName}",
            a.ApplicantResponses.Max(r => r.SubmittedAt))).ToList();

        return (items, totalCount);
    }

    public async Task<ReviewApplicationDto?> GetApplicationForReviewAsync(int applicationId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null)
            return null;

        // Transition to UnderReview if currently Submitted
        if (application.State == ApplicationState.Submitted)
        {
            application.StartReview();
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
        }

        return MapToReviewDto(application);
    }

    public async Task<UserFacingError?> ReviewItemAsync(int applicationId, int itemId, string decision, string? comment, int? selectedSupplierId, string userId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null) return UserFacingError.From(UserFacingErrorCode.ApplicationNotFound);
        if (application.State != ApplicationState.UnderReview)
            return UserFacingError.From(UserFacingErrorCode.ApplicationNotUnderReview);

        var item = application.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return UserFacingError.From(UserFacingErrorCode.ApplicationItemNotFound);

        try
        {
            switch (decision)
            {
                case "Approve":
                    if (!selectedSupplierId.HasValue)
                        return UserFacingError.From(UserFacingErrorCode.SupplierRequiredOnApprove);
                    item.Approve(selectedSupplierId.Value, comment);
                    break;
                case "Reject":
                    item.Reject(comment);
                    break;
                case "RequestMoreInfo":
                    item.RequestMoreInfo(comment);
                    break;
                default:
                    return UserFacingError.From(UserFacingErrorCode.InvalidReviewDecision, decision);
            }

            application.AddVersionHistory(new VersionHistory(userId, "ReviewItem",
                $"Item '{item.ProductName}' — {decision}"));
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification);
        }
    }

    public async Task<UserFacingError?> FlagTechnicalEquivalenceAsync(int applicationId, int itemId, bool isNotEquivalent, string userId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null) return UserFacingError.From(UserFacingErrorCode.ApplicationNotFound);
        if (application.State != ApplicationState.UnderReview)
            return UserFacingError.From(UserFacingErrorCode.ApplicationNotUnderReview);

        var item = application.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return UserFacingError.From(UserFacingErrorCode.ApplicationItemNotFound);

        try
        {
            if (isNotEquivalent)
                item.FlagNotEquivalent();
            else
                item.ClearNotEquivalentFlag();

            application.AddVersionHistory(new VersionHistory(userId, "FlagEquivalence",
                $"Item '{item.ProductName}' — {(isNotEquivalent ? "flagged" : "cleared")} technical equivalence"));
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
            return null;
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification);
        }
    }

    public async Task<UserFacingError?> SendBackAsync(int applicationId, string userId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null) return UserFacingError.From(UserFacingErrorCode.ApplicationNotFound);

        try
        {
            application.SendBack();
            application.AddVersionHistory(new VersionHistory(userId, "SendBack",
                "Application sent back to applicant for more information"));
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification);
        }
    }

    public async Task<(UserFacingError? Error, List<string>? UnresolvedItems)> FinalizeReviewAsync(int applicationId, bool force, string userId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null)
            return (UserFacingError.From(UserFacingErrorCode.ApplicationNotFound), null);

        try
        {
            application.Finalize(force);
            application.AddVersionHistory(new VersionHistory(userId, "Finalize",
                $"Review finalized{(force ? " (force — unresolved items implicitly rejected)" : "")}"));
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();
            return (null, null);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("unresolved"))
        {
            var unresolvedItems = application.Items
                .Where(i => i.ReviewStatus == ItemReviewStatus.Pending
                         || i.ReviewStatus == ItemReviewStatus.NeedsInfo)
                .Select(i => i.ProductName)
                .ToList();
            return (null, unresolvedItems);
        }
        catch (InvalidOperationException ex)
        {
            return (UserFacingError.From(UserFacingErrorCode.OperationRejected, ex.Message), null);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            return (UserFacingError.From(UserFacingErrorCode.ConcurrentApplicationModification), null);
        }
    }

    private static ReviewApplicationDto MapToReviewDto(AppEntity application)
    {
        var reviewItems = application.Items.Select(item =>
        {
            var quotations = item.Quotations.ToList();
            // Spec 013 R5: SupplierScore signature now requires (Q, Supplier, Branch).
            // The branch is reserved for reviewer-UI display use; score math is unchanged.
            var scoreInputs = quotations
                .Where(q => q.Supplier is not null)
                .Select(q => (q, q.Supplier!, (SupplierBranch?)q.SupplierBranch))
                .ToList();
            var scoreResults = SupplierScore.ComputeForItem(scoreInputs);
            var scoreMap = scoreResults.ToDictionary(s => s.QuotationId, s => s.Score);

            var quotationDtos = quotations.Select(q =>
            {
                var score = scoreMap.GetValueOrDefault(q.Id);
                return new ReviewQuotationDto(
                    q.Id,
                    q.SupplierId,
                    q.Supplier?.Name ?? string.Empty,
                    q.Supplier?.LegalId ?? string.Empty,
                    q.Price,
                    q.ValidUntil,
                    q.Document?.OriginalFileName ?? string.Empty,
                    score?.IsRecommended ?? false,
                    score?.Total ?? 0,
                    score?.IsCompliantCCSS ?? false,
                    score?.IsCompliantHacienda ?? false,
                    score?.IsCompliantSICOP ?? false,
                    score?.HasElectronicInvoice ?? false,
                    score?.HasLowestPrice ?? false,
                    score?.IsPreSelected ?? false,
                    score?.IsSupplierVerified ?? false,
                    score?.IsSupplierRejected ?? false,
                    Currency: string.IsNullOrEmpty(q.Currency) ? "CRC" : q.Currency,
                    ConvertedCrcAmount: q.ConvertedCrcAmount,
                    SnapshotRateValue: q.Snapshot?.RateValue,
                    SnapshotRateType: q.Snapshot?.RateType.ToString(),
                    SnapshotEffectiveAtUtc: q.Snapshot?.EffectiveAtUtc,
                    LegacyNeedsReview: q.LegacyNeedsReview);
            })
            .OrderByDescending(q => q.Score)
            .ToList();

            return new ReviewItemDto(
                item.Id,
                item.ProductName,
                item.Category?.Name ?? string.Empty,
                item.TechnicalSpecifications,
                item.ReviewStatus,
                item.ReviewComment,
                item.SelectedSupplierId,
                item.IsNotTechnicallyEquivalent,
                quotationDtos,
                item.Impact?.ImpactTemplate?.Name,
                item.Impact?.ParameterValues.Select(pv => new ImpactParameterDisplayDto(
                    pv.ImpactTemplateParameter?.Name ?? string.Empty,
                    pv.ImpactTemplateParameter?.DisplayLabel ?? string.Empty,
                    pv.Value ?? string.Empty)).ToList() ?? []);
        }).ToList();

        // Spec 013 FR-052: count distinct quotations referencing a Rejected supplier
        // for the reviewer banner.
        var rejectedSupplierCount = application.Items
            .SelectMany(i => i.Quotations)
            .Where(q => q.Supplier?.VerificationStatus == SupplierVerificationStatus.Rejected)
            .Count();

        return new ReviewApplicationDto(
            application.Id,
            $"{application.Applicant.FirstName} {application.Applicant.LastName}",
            application.Applicant.PerformanceScore,
            application.State,
            application.SubmittedAt,
            reviewItems,
            rejectedSupplierCount);
    }
}
