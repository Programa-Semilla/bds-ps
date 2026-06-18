using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 040 / D4–D5 — an admin-configured checklist applied at a workflow stage
/// (<see cref="ChecklistStage"/>). Owns an ordered set of text-only
/// <see cref="ChecklistTemplateItem"/> children, mirroring the
/// <see cref="Category"/> template-with-items pattern (spec 035). At most one
/// template may be active per effective stage — enforced by the admin service.
/// </summary>
public class ChecklistTemplate
{
    private readonly List<ChecklistTemplateItem> _items = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ChecklistStage AppliesToStage { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<ChecklistTemplateItem> Items => _items.AsReadOnly();

    private ChecklistTemplate() { }

    public ChecklistTemplate(
        string name,
        string? description,
        ChecklistStage appliesToStage,
        bool isActive,
        string createdByUserId)
    {
        Name = name;
        Description = description;
        AppliesToStage = appliesToStage;
        IsActive = isActive;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(string name, string? description, ChecklistStage appliesToStage)
    {
        Name = name;
        Description = description;
        AppliesToStage = appliesToStage;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void AddItem(string text, int displayOrder, bool isRequired)
    {
        _items.Add(new ChecklistTemplateItem(text, displayOrder, isRequired, isActive: true));
    }

    public void ClearItems() => _items.Clear();
}
