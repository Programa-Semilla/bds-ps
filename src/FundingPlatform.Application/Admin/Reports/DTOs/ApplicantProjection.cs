namespace FundingPlatform.Application.Admin.Reports.DTOs;

/// <summary>
/// Flat per-applicant projection used internally by AdminReportsService to compose
/// ApplicantRowDto. Per-currency Approved/Executed stacks are computed via a
/// per-page follow-up query keyed by ApplicantId.
/// </summary>
public sealed record ApplicantProjection(
    int ApplicantId,
    string FullName,
    string LegalId,
    string Email,
    string? UserCode,
    int TotalApps,
    int ResolvedCount,
    int ResponseFinalizedCount,
    int AgreementExecutedCount,
    int TotalItems,
    int ApprovedItems,
    DateTime? LastActivity);
