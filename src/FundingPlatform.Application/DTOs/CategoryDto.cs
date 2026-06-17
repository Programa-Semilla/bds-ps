namespace FundingPlatform.Application.DTOs;

public record CategoryDto(
    int Id,
    string Name,
    string? Description,
    // Spec 035 / US1 — admin list extras (defaulted so applicant-facing
    // positional constructions stay valid).
    bool IsActive = true,
    int FieldCount = 0);

/// <summary>
/// Spec 035 / US1 — a category with its full ordered field set, for the admin
/// edit form. Mirrors ImpactTemplateDto + parameters.
/// </summary>
public record CategoryDetailDto(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    List<CategoryFieldDefinitionDto> Fields);

public record CategoryFieldDefinitionDto(
    int Id,
    string Name,
    string DisplayLabel,
    string DataType,
    bool IsRequired,
    int SortOrder);
