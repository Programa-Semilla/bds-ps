namespace FundingPlatform.Application.Admin.Users.DTOs;

/// <summary>
/// Spec 037 — read projection of an applicant's <c>Company</c> for the admin
/// Edit management card (active + archived).
/// </summary>
public record CompanyDto(int Id, string Name, bool IsArchived);
