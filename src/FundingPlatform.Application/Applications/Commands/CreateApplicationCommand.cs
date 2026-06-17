namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 037 / FR-002 — the applicant selects an admin-assigned <c>Company</c> at
/// creation (controlled dropdown, replacing the spec-018 free-text company name).
/// The service resolves the company by id (ownership + active validation, FR-018/019)
/// and snapshots its name into <c>Application.CompanyName</c>.
///
/// Spec 029 / FR-017 / FR-018 — the required <c>GroupId</c> anchor chosen at
/// creation (the applicant's eligible Group under an Active Process + Active Fund).
/// </summary>
public record CreateApplicationCommand(int ApplicantId, int CompanyId, int GroupId);
