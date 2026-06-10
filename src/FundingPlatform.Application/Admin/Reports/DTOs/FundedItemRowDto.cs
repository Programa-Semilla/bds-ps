using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Reports.DTOs;

public sealed record FundedItemRowDto(
    int AppId,
    string ApplicantFullName,
    string ItemProductName,
    string CategoryName,
    string SupplierName,
    string? SupplierLegalId,
    decimal Price,
    string Currency,
    ApplicationState AppState,
    DateTime? AppSubmittedAt,
    DateTime? ApprovedAt,
    bool HasAgreement,
    bool Executed,
    string FundName,
    // Spec 015 / T416 — appended to the CSV; null on legacy rows that
    // never received a snapshot (FR-026).
    decimal? ConvertedCrcAmount = null);
