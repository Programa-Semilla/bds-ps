using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.DTOs;

/// <summary>
/// Spec 027 / US4 — the single read-only per-line decision shape rendered
/// identically on all five interaction surfaces (reviewer review, applicant
/// accept/reject, and the funding-agreement Details page across its
/// generate / signing / signed-review states). Deliberately lean: no AI
/// comparison, supplier scores, or impact parameters (constitution VI / YAGNI).
/// </summary>
public sealed record DecisionSummaryLineDto(
    string? LineCode,
    string ProductName,
    string CategoryName,
    string TechnicalSpecifications,
    ItemReviewStatus ReviewStatus,
    string? ReviewComment,
    string? ApprovedSupplierName,
    DecisionSummaryQuotationView? ApprovedAmount,
    IReadOnlyList<DecisionSummaryQuotationView> Quotations,
    string? ApplicantDecision);

/// <summary>
/// Spec 027 / US4 — one quoted option for a line: amount in its own currency
/// plus the spec-015 CRC conversion note (null for CRC quotes).
/// </summary>
public sealed record DecisionSummaryQuotationView(
    string SupplierName,
    decimal Amount,
    string Currency,
    decimal? ConvertedCrcAmount,
    string? CurrencyConversionNote);
