namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 040 / D5 — a single text-only verification line owned by a
/// <see cref="ChecklistTemplate"/>. Mirrors <see cref="CategoryField"/>:
/// ordered (<see cref="DisplayOrder"/>), optionally <see cref="IsRequired"/> (gates
/// count required items only), and soft-deactivatable (<see cref="IsActive"/> — an
/// inactive item is neither shown nor required). The completed outcome is recorded on
/// an <see cref="ApplicationChecklistResponse"/>, which snapshots <see cref="Text"/>.
/// </summary>
public class ChecklistTemplateItem
{
    public int Id { get; private set; }
    public int ChecklistTemplateId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; }

    public ChecklistTemplate ChecklistTemplate { get; private set; } = null!;

    private ChecklistTemplateItem() { }

    public ChecklistTemplateItem(string text, int displayOrder, bool isRequired, bool isActive)
    {
        Text = text;
        DisplayOrder = displayOrder;
        IsRequired = isRequired;
        IsActive = isActive;
    }

    /// <summary>Spec 040 / FR-003 — soft-deactivate on template edit (never hard-deleted while referenced).</summary>
    public void Deactivate() => IsActive = false;
}
