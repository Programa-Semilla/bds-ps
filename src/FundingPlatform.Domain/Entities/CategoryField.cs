using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 035 / data-model.md (D1) — a single admin-configured field that a
/// <see cref="Category"/> collects on every line item assigned to it. Mirrors
/// <see cref="ImpactTemplateParameter"/> (the proven impact-template pattern),
/// minus the dormant <c>ValidationRules</c> seam (out of scope). The applicant's
/// entered value lands in <see cref="CategoryFieldValue"/>, keyed by item.
/// </summary>
public class CategoryField
{
    public int Id { get; private set; }
    public int CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DisplayLabel { get; private set; } = string.Empty;
    public ParameterDataType DataType { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }

    public Category Category { get; private set; } = null!;

    private CategoryField() { }

    public CategoryField(
        string name,
        string displayLabel,
        ParameterDataType dataType,
        bool isRequired,
        int sortOrder)
    {
        Name = name;
        DisplayLabel = displayLabel;
        DataType = dataType;
        IsRequired = isRequired;
        SortOrder = sortOrder;
    }
}
