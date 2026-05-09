namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 018 / FR-015 — capture the commercial entity name (`Empresa solicitante`)
/// at Application creation. The field is non-nullable from day one (no production
/// data → no migration shim) so the command record gains a required <c>CompanyName</c>.
/// </summary>
public record CreateApplicationCommand(int ApplicantId, string CompanyName);
