namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 018 / FR-015 — capture the commercial entity name (`Empresa solicitante`)
/// at Application creation. The field is non-nullable from day one (no production
/// data → no migration shim) so the command record gains a required <c>CompanyName</c>.
///
/// Spec 029 / FR-017 / FR-018 — adds the required <c>GroupId</c> anchor chosen at
/// creation (the applicant's eligible Group under an Active Process + Active Fund).
/// </summary>
public record CreateApplicationCommand(int ApplicantId, string CompanyName, int GroupId);
