namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 035 (evolved 2026-06-16, D14 / SC-007) — removes a declared application
/// impact; the domain also strips every line item's attribution to it.
/// </summary>
public record RemoveApplicationImpactCommand(int ApplicationId, int ApplicationImpactId);
