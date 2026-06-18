using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.DTOs;

public record ReviewApplicationDto(
    int ApplicationId,
    string ApplicantName,
    decimal? ApplicantPerformanceScore,
    ApplicationState State,
    DateTime? SubmittedAt,
    List<ReviewItemDto> Items,
    // Spec 035 (evolved 2026-06-16, D16) — the application's declared impacts (app level).
    List<ReviewImpactGroupDto> Impacts,
    int RejectedSupplierCount = 0);

public record ReviewItemDto(
    int ItemId,
    string ProductName,
    string CategoryName,
    ItemReviewStatus ReviewStatus,
    string? ReviewComment,
    int? SelectedSupplierId,
    bool IsNotTechnicallyEquivalent,
    List<ReviewQuotationDto> Quotations,
    // Spec 035 (evolved 2026-06-16, D14) — attributed impact names + short justification.
    List<string> AttributedImpactNames,
    string? ImpactJustification,
    // Spec 035 / D1 — per-item category field label/value pairs.
    List<CategoryFieldValueDto> CategoryFields,
    /// <summary>Spec 018 / FR-012 — reviewer-assigned line code, null until first
    /// assignment.</summary>
    string? LineCode = null);

// Spec 035 (evolved 2026-06-16, D16) — one declared application impact for display.
public record ReviewImpactGroupDto(
    string TemplateName,
    List<ImpactParameterDisplayDto> Parameters);

public record ReviewQuotationDto(
    int QuotationId,
    int SupplierId,
    string SupplierName,
    string SupplierLegalId,
    decimal Price,
    DateOnly ValidUntil,
    string DocumentFileName,
    bool IsRecommended,
    int Score,
    bool ScoreCCSS,
    bool ScoreHacienda,
    bool ScoreSICOP,
    bool ScoreLowestPrice,
    bool IsPreSelected,
    bool IsSupplierVerified = false,
    bool IsSupplierRejected = false,
    // Spec 015 / T415 — multi-currency surface on the review screen so the
    // reviewer sees the original currency + converted CRC + indicator without
    // leaving the page.
    string Currency = "CRC",
    decimal? ConvertedCrcAmount = null,
    decimal? SnapshotRateValue = null,
    string? SnapshotRateType = null,
    DateTime? SnapshotEffectiveAtUtc = null,
    bool LegacyNeedsReview = false,
    // Spec 038 (US3) — provider warning + compliance/freshness snapshot for reviewers.
    SupplierComplianceSnapshot? Compliance = null);

public record ImpactParameterDisplayDto(
    string Name,
    string DisplayLabel,
    string Value);
