namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 035 / US2 — adding a line item now carries the selected category's field
/// values (keyed by CategoryFieldId) and the per-item impact template + parameter
/// values (keyed by ImpactTemplateParameterId). TechnicalSpecifications is gone.
/// <see cref="ImpactTemplateId"/> is null when no active impact template exists
/// (research D7 empty-state); the submit gate then blocks the application.
/// </summary>
public record AddItemCommand(
    int ApplicationId,
    string ProductName,
    int CategoryId,
    Dictionary<int, string?> CategoryFieldValues,
    int? ImpactTemplateId,
    Dictionary<int, string?> ImpactParameterValues);
