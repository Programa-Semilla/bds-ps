using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Reports.DTOs;

/// <summary>
/// Flat per-application projection used internally by AdminReportsService to compose
/// ApplicationRowDto. The per-currency Approved stack is computed separately via a
/// per-page follow-up query keyed by AppId.
/// </summary>
public sealed record ApplicationProjection(
    int AppId,
    string ApplicantFullName,
    string ApplicantLegalId,
    ApplicationState State,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime UpdatedAt,
    int ItemCount,
    bool HasAgreement,
    bool HasActiveAppeal,
    string FundName);
