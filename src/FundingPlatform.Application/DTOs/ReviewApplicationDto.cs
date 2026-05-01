using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.DTOs;

public record ReviewApplicationDto(
    int ApplicationId,
    string ApplicantName,
    decimal? ApplicantPerformanceScore,
    ApplicationState State,
    DateTime? SubmittedAt,
    List<ReviewItemDto> Items,
    int RejectedSupplierCount = 0);

public record ReviewItemDto(
    int ItemId,
    string ProductName,
    string CategoryName,
    string TechnicalSpecifications,
    ItemReviewStatus ReviewStatus,
    string? ReviewComment,
    int? SelectedSupplierId,
    bool IsNotTechnicallyEquivalent,
    List<ReviewQuotationDto> Quotations,
    string? ImpactTemplateName,
    List<ImpactParameterDisplayDto> ImpactParameters);

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
    bool ScoreElectronicInvoice,
    bool ScoreLowestPrice,
    bool IsPreSelected,
    bool IsSupplierVerified = false,
    bool IsSupplierRejected = false);

public record ImpactParameterDisplayDto(
    string Name,
    string DisplayLabel,
    string Value);
