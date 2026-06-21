using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 040 / FR-007 — maps a <see cref="ReviewApplicationDto"/> to the
/// <see cref="ReviewApplicationViewModel"/>. Extracted from <c>ReviewController</c> so the
/// reviewer Review surface and the auditor Audit surface project the application identically
/// (the auditor gets reviewer-equivalent read access). Decision-summary and AI-comparison
/// hydration stay in the respective controllers (they need request-scoped services).
/// </summary>
public static class ReviewApplicationViewModelMapper
{
    public static ReviewApplicationViewModel Map(ReviewApplicationDto dto)
    {
        var hasUnresolved = dto.Items.Any(i =>
            i.ReviewStatus == Domain.Enums.ItemReviewStatus.Pending ||
            i.ReviewStatus == Domain.Enums.ItemReviewStatus.NeedsInfo);

        return new ReviewApplicationViewModel
        {
            ApplicationId = dto.ApplicationId,
            ApplicantName = dto.ApplicantName,
            ApplicantPerformanceScore = dto.ApplicantPerformanceScore,
            State = dto.State.ToString(),
            SubmittedAt = dto.SubmittedAt,
            HasUnresolvedItems = hasUnresolved,
            RejectedSupplierCount = dto.RejectedSupplierCount,
            Items = dto.Items.Select(item => new ReviewItemViewModel
            {
                ItemId = item.ItemId,
                ProductName = item.ProductName,
                CategoryName = item.CategoryName,
                ReviewStatus = item.ReviewStatus.ToString(),
                ReviewComment = item.ReviewComment,
                SelectedSupplierId = item.SelectedSupplierId,
                IsNotTechnicallyEquivalent = item.IsNotTechnicallyEquivalent,
                LineCode = item.LineCode,
                HasRecommendationTie = item.HasRecommendationTie,
                HasAnyEligible = item.HasAnyEligible,
                AttributedImpactNames = item.AttributedImpactNames,
                ImpactJustification = item.ImpactJustification,
                Quotations = item.Quotations.Select(q => new ReviewQuotationViewModel
                {
                    QuotationId = q.QuotationId,
                    SupplierId = q.SupplierId,
                    SupplierName = q.SupplierName,
                    SupplierLegalId = q.SupplierLegalId,
                    Price = q.Price,
                    ValidUntil = q.ValidUntil,
                    DocumentFileName = q.DocumentFileName,
                    IsRecommended = q.IsRecommended,
                    IsEligible = q.IsEligible,
                    BlockReason = q.BlockReason,
                    Total = q.Total,
                    PriceScore = q.PriceScore,
                    DeliveryLeadTimeScore = q.DeliveryLeadTimeScore,
                    WarrantyTimeScore = q.WarrantyTimeScore,
                    HaciendaScore = q.HaciendaScore,
                    CcssScore = q.CcssScore,
                    SicopScore = q.SicopScore,
                    PmeOrPymeScore = q.PmeOrPymeScore,
                    DeliveryLeadTimeValue = q.DeliveryLeadTimeValue,
                    DeliveryLeadTimeUnit = q.DeliveryLeadTimeUnit,
                    WarrantyValue = q.WarrantyValue,
                    WarrantyUnit = q.WarrantyUnit,
                    IsSupplierVerified = q.IsSupplierVerified,
                    IsSupplierRejected = q.IsSupplierRejected,
                    Currency = q.Currency,
                    ConvertedCrcAmount = q.ConvertedCrcAmount,
                    SnapshotRateValue = q.SnapshotRateValue,
                    SnapshotRateType = q.SnapshotRateType,
                    SnapshotEffectiveAtUtc = q.SnapshotEffectiveAtUtc,
                    Compliance = q.Compliance,
                    LegacyNeedsReview = q.LegacyNeedsReview,
                }).ToList(),
                // Spec 035 / D1 — per-item category field values.
                CategoryFields = item.CategoryFields.Select(cf => new CategoryFieldDisplayViewModel
                {
                    Label = cf.Label,
                    Value = cf.Value,
                }).ToList()
            }).ToList(),
            // Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts.
            Impacts = dto.Impacts.Select(ai => new ApplicationImpactDisplayViewModel
            {
                TemplateName = ai.TemplateName,
                Parameters = ai.Parameters.Select(p => new ImpactParameterDisplayViewModel
                {
                    Name = p.Name,
                    DisplayLabel = p.DisplayLabel,
                    Value = p.Value,
                }).ToList(),
            }).ToList(),
        };
    }
}
