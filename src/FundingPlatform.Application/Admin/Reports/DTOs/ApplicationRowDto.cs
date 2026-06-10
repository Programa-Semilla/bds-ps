using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Admin.Reports.DTOs;

public sealed record ApplicationRowDto(
    int AppId,
    string ApplicantFullName,
    string ApplicantLegalId,
    ApplicationState State,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? ResolvedAt,
    int ItemCount,
    IReadOnlyList<CurrencyAmount> TotalApproved,
    bool HasAgreement,
    bool HasActiveAppeal,
    string FundName);
