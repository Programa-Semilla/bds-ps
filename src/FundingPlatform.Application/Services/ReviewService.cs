using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Services;

public class ReviewService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly INotificationOutboxWriter _outboxWriter;
    private readonly IWorkflowTransactionScope _txScope;
    private readonly ILogger<ReviewService> _logger;

    private const int PageSize = 25;

    public ReviewService(
        IApplicationRepository applicationRepository,
        INotificationOutboxWriter outboxWriter,
        IWorkflowTransactionScope txScope,
        ILogger<ReviewService> logger)
    {
        _applicationRepository = applicationRepository;
        _outboxWriter = outboxWriter;
        _txScope = txScope;
        _logger = logger;
    }

    /// <summary>
    /// Spec 020 — resolves the parent ApplicationId for a given ApplicationItemId
    /// so the comparison endpoints can run the group-scope guard against the
    /// application (FR-A1). Returns null when the item id is unknown.
    /// </summary>
    public async Task<int?> GetApplicationIdForItemAsync(int applicationItemId, CancellationToken ct)
    {
        // Walk via the existing GetByIdWithDetails path — the application
        // repository already projects Items so the join is local memory.
        // Cheap O(applications) scan for MVP; can be optimized to a single
        // EF projection later.
        return await _applicationRepository.GetApplicationIdForItemAsync(applicationItemId, ct);
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

    /// <summary>
    /// Spec 018 / FR-012 / FR-013 / FR-014 — reviewer records a per-item decision
    /// and assigns the line code that surfaces in the Funding Agreement PDF tables.
    /// LineCode is required when <paramref name="decision"/> is <c>Approve</c> or
    /// <c>Reject</c>; for <c>RequestMoreInfo</c> a blank LineCode is allowed (the
    /// reviewer is iterating before deciding). Both calls run in the same UoW so a
    /// duplicate-LineCode error rolls back the decision write.
    /// </summary>
    public async Task<UserFacingError?> ReviewItemAsync(
        int applicationId,
        int itemId,
        string decision,
        string? comment,
        int? selectedSupplierId,
        string? lineCode,
        string userId)
    {
        var application = await _applicationRepository.GetByIdWithDetailsAsync(applicationId);
        if (application is null) return UserFacingError.From(UserFacingErrorCode.ApplicationNotFound);
        if (application.State != ApplicationState.UnderReview)
            return UserFacingError.From(UserFacingErrorCode.ApplicationNotUnderReview);

        var item = application.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return UserFacingError.From(UserFacingErrorCode.ApplicationItemNotFound);

        // FR-012: Approve / Reject require a non-blank line code; RequestMoreInfo allows
        // blank because the reviewer hasn't decided on a code yet (R-008).
        var requiresLineCode = decision is "Approve" or "Reject";
        var trimmedLineCode = (lineCode ?? string.Empty).Trim();
        if (requiresLineCode && trimmedLineCode.Length == 0)
        {
            return UserFacingError.From(UserFacingErrorCode.LineCodeRequired);
        }

        try
        {
            // Assign the line code first so an aggregate-level rejection (duplicate
            // / over-length) short-circuits before we mutate the decision state.
            // For RequestMoreInfo with a non-blank code the reviewer also gets to
            // record it; for blank we skip the call.
            if (trimmedLineCode.Length > 0)
            {
                try
                {
                    application.AssignLineCodeToItem(itemId, trimmedLineCode);
                }
                catch (ArgumentException ex)
                {
                    // Read the stable Data["FundingPlatform.ValidationReason"] marker
                    // the entity sets instead of brittle message-string matching.
                    // Renaming the English validation message no longer silently
                    // miscategorises the user-facing error code (FR-014 / NFR-001).
                    var reason = ex.Data[Item.ValidationReasonKey] as string;
                    var code = reason switch
                    {
                        Item.LineCodeTooLongReason => UserFacingErrorCode.LineCodeTooLong,
                        Item.LineCodeRequiredReason => UserFacingErrorCode.LineCodeRequired,
                        _ => UserFacingErrorCode.LineCodeRequired,
                    };
                    return UserFacingError.From(code, ex.Message);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already assigned", StringComparison.Ordinal))
                {
                    return UserFacingError.From(UserFacingErrorCode.LineCodeDuplicate, ex.Message);
                }
            }

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
            var vhRow = new VersionHistory(userId, "SendBack",
                "Application sent back to applicant for more information");
            application.AddVersionHistory(vhRow);

            // Spec 021 / FR-001 — two-phase save (workflow first, outbox second).
            // See ApplicationService.SubmitApplicationAsync for the rationale on
            // not using an explicit transaction with Aspire's SqlClient retry policy.
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            var stageGroupIds = await _outboxWriter.GetApplicantStageGroupIdsAsync(
                application.Id, CancellationToken.None);
            var applicantDisplayName = application.Applicant is not null
                ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
                : "Solicitante";
            var applicantUserId = application.Applicant?.UserId ?? string.Empty;
            var payload = new NotificationPayload(
                application.Id, applicantUserId, applicantDisplayName,
                stageGroupIds, OutcomeCode: null);

            await _outboxWriter.EnqueueAsync(
                NotificationEvent.ReturnedToApplicant,
                application.Id, vhRow.Id, payload, CancellationToken.None);

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
            var vhRow = new VersionHistory(userId, "Finalize",
                $"Review finalized{(force ? " (force — unresolved items implicitly rejected)" : "")}");
            application.AddVersionHistory(vhRow);

            // Spec 021 / US4 + US5 / R-004 — derive terminal outcome from per-item
            // decisions: every required item Approved → Approved; otherwise Rejected.
            var allApproved = application.Items.All(i => i.ReviewStatus == ItemReviewStatus.Approved);
            var outcomeEvent = allApproved
                ? NotificationEvent.ApplicationApproved
                : NotificationEvent.ApplicationRejected;
            var outcomeCode = allApproved ? "Approved" : "Rejected";

            // Spec 021 / FR-001 — two-phase save. See SubmitApplicationAsync rationale.
            await _applicationRepository.UpdateAsync(application);
            await _applicationRepository.SaveChangesAsync();

            var stageGroupIds = await _outboxWriter.GetApplicantStageGroupIdsAsync(
                application.Id, CancellationToken.None);
            var applicantDisplayName = application.Applicant is not null
                ? $"{application.Applicant.FirstName} {application.Applicant.LastName}".Trim()
                : "Solicitante";
            var applicantUserId = application.Applicant?.UserId ?? string.Empty;
            var payload = new NotificationPayload(
                application.Id, applicantUserId, applicantDisplayName,
                stageGroupIds, OutcomeCode: outcomeCode);

            await _outboxWriter.EnqueueAsync(
                outcomeEvent, application.Id, vhRow.Id, payload, CancellationToken.None);

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
            // Spec 035 (evolved 2026-06-16, D14) — attributed impact names + justification.
            var itemAttributedImpactNames = item.ItemImpacts
                .Select(ii => ii.ApplicationImpact?.ImpactTemplate?.Name ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToList();
            // Spec 035 / D1 — per-item category field label/value pairs.
            var itemCategoryFields = item.CategoryFieldValues
                .OrderBy(cfv => cfv.CategoryField?.SortOrder ?? 0)
                .Select(cfv => new CategoryFieldValueDto(
                    cfv.CategoryField?.DisplayLabel ?? string.Empty,
                    cfv.Value))
                .ToList();
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
                    score?.HasLowestPrice ?? false,
                    score?.IsPreSelected ?? false,
                    score?.IsSupplierVerified ?? false,
                    score?.IsSupplierRejected ?? false,
                    Currency: string.IsNullOrEmpty(q.Currency) ? "CRC" : q.Currency,
                    ConvertedCrcAmount: q.ConvertedCrcAmount,
                    SnapshotRateValue: q.Snapshot?.RateValue,
                    SnapshotRateType: q.Snapshot?.RateType.ToString(),
                    SnapshotEffectiveAtUtc: q.Snapshot?.EffectiveAtUtc,
                    LegacyNeedsReview: q.LegacyNeedsReview,
                    // Spec 038 (US3) — provider warning + compliance/freshness for reviewers.
                    Compliance: q.Supplier is null ? null : new SupplierComplianceSnapshot(
                        HasWarning: q.Supplier.HasWarning,
                        WarningNote: q.Supplier.WarningNote,
                        Hacienda: q.Supplier.HaciendaStatus,
                        HaciendaReviewedAt: q.Supplier.HaciendaLastReviewedAt,
                        HaciendaSource: q.Supplier.HaciendaLastReviewedSource,
                        Ccss: q.Supplier.CcssStatus,
                        CcssReviewedAt: q.Supplier.CcssLastReviewedAt,
                        CcssSource: q.Supplier.CcssLastReviewedSource,
                        Sicop: q.Supplier.SicopStatus,
                        SicopReviewedAt: q.Supplier.SicopLastReviewedAt,
                        SicopSource: q.Supplier.SicopLastReviewedSource));
            })
            .OrderByDescending(q => q.Score)
            .ToList();

            return new ReviewItemDto(
                item.Id,
                item.ProductName,
                item.Category?.Name ?? string.Empty,
                item.ReviewStatus,
                item.ReviewComment,
                item.SelectedSupplierId,
                item.IsNotTechnicallyEquivalent,
                quotationDtos,
                itemAttributedImpactNames,
                item.ImpactJustification,
                itemCategoryFields,
                LineCode: item.LineCode);
        }).ToList();

        // Spec 013 FR-052: count distinct quotations referencing a Rejected supplier
        // for the reviewer banner.
        var rejectedSupplierCount = application.Items
            .SelectMany(i => i.Quotations)
            .Where(q => q.Supplier?.VerificationStatus == SupplierVerificationStatus.Rejected)
            .Count();

        // Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts (app level).
        var impacts = application.Impacts.Select(ai => new ReviewImpactGroupDto(
            ai.ImpactTemplate?.Name ?? string.Empty,
            ai.ParameterValues.Select(pv => new ImpactParameterDisplayDto(
                pv.ImpactTemplateParameter?.Name ?? string.Empty,
                pv.ImpactTemplateParameter?.DisplayLabel ?? string.Empty,
                pv.Value ?? string.Empty)).ToList())).ToList();

        return new ReviewApplicationDto(
            application.Id,
            $"{application.Applicant.FirstName} {application.Applicant.LastName}",
            application.Applicant.PerformanceScore,
            application.State,
            application.SubmittedAt,
            reviewItems,
            impacts,
            rejectedSupplierCount);
    }
}
