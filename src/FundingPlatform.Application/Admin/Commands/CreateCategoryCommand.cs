namespace FundingPlatform.Application.Admin.Commands;

/// <summary>
/// Spec 035 / US1 — admin category-field configuration. Mirrors
/// CreateImpactTemplateCommand + ParameterDefinition.
/// </summary>
public record CreateCategoryCommand(
    string Name,
    string? Description,
    List<CategoryFieldDefinition> Fields);

public record UpdateCategoryCommand(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    List<CategoryFieldDefinition> Fields);

public record CategoryFieldDefinition(
    string Name,
    string DisplayLabel,
    string DataType,
    bool IsRequired,
    int SortOrder);
