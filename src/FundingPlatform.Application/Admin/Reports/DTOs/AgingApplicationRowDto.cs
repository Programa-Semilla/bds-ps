using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Admin.Reports.DTOs;

public sealed record AgingApplicationRowDto(
    int AppId,
    string ApplicantFullName,
    string ApplicantEmail,
    string ApplicantLegalId,
    ApplicationState State,
    int DaysInCurrentState,
    DateTime? LastTransitionAt,
    string? LastActor,
    int ItemCount,
    IReadOnlyList<CurrencyAmount> TotalApproved,
    string FundName);
