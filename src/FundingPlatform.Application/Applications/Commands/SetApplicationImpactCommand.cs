namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 021 / FR-005 — Impact is a per-Application concern captured upfront,
/// before any Item exists. No <c>ItemId</c>: the command targets the
/// Application aggregate directly.
/// </summary>
public record SetApplicationImpactCommand(
    int ApplicationId,
    int ImpactTemplateId,
    Dictionary<int, string?> ParameterValues);
