namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 035 (evolved 2026-06-16, US3) — adding a line item carries the selected
/// category's field values (keyed by CategoryFieldId), the attribution to the
/// application's declared impacts (<see cref="ApplicationImpactIds"/>), and a short
/// justification. The line item no longer carries its own impact template/values.
/// TechnicalSpecifications is gone.
/// </summary>
public record AddItemCommand(
    int ApplicationId,
    string ProductName,
    int CategoryId,
    Dictionary<int, string?> CategoryFieldValues,
    IReadOnlyList<int> ApplicationImpactIds,
    string? ImpactJustification);
