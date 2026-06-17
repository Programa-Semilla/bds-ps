namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 035 (evolved 2026-06-16, D13 / FR-006) — declares an impact on the
/// application: a chosen (active) impact template plus its parameter values
/// (keyed by ImpactTemplateParameterId). Required values are validated.
/// </summary>
public record AddApplicationImpactCommand(
    int ApplicationId,
    int ImpactTemplateId,
    Dictionary<int, string?> ParameterValues);
