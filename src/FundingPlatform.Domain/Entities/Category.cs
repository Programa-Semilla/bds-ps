using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 035 / data-model.md (D1) — a submission category that owns an
/// admin-configured field set (<see cref="CategoryField"/>) rendered dynamically
/// when an applicant assigns a line item to it. Gains mutators mirroring
/// <see cref="ImpactTemplate"/> (it had none before — only a constructor).
/// </summary>
public class Category
{
    private readonly List<CategoryField> _fields = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyList<CategoryField> Fields => _fields.AsReadOnly();

    private Category() { }

    public Category(string name, string? description, bool isActive)
    {
        Name = name;
        Description = description;
        IsActive = isActive;
    }

    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void AddField(
        string name,
        string displayLabel,
        ParameterDataType dataType,
        bool isRequired,
        int sortOrder)
    {
        _fields.Add(new CategoryField(name, displayLabel, dataType, isRequired, sortOrder));
    }

    public void ClearFields() => _fields.Clear();
}
