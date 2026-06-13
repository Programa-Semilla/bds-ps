namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 035 / data-model.md (D1) — applicant-entered value for one
/// <see cref="CategoryField"/> on one line item (EAV). Mirrors
/// <see cref="ImpactParameterValue"/> but keyed by item (the category is chosen
/// per item). Value is stored as a string; type coercion is the app layer's job.
/// Held by <see cref="Item"/> via its <c>CategoryFieldValues</c> collection.
/// </summary>
public class CategoryFieldValue
{
    public int Id { get; private set; }
    public int ItemId { get; private set; }
    public int CategoryFieldId { get; private set; }
    public string? Value { get; private set; }

    public CategoryField CategoryField { get; private set; } = null!;

    private CategoryFieldValue() { }

    public CategoryFieldValue(int categoryFieldId, string? value)
    {
        CategoryFieldId = categoryFieldId;
        Value = value;
    }
}
