namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 035 (evolved 2026-06-16, data-model.md D14) — per-item impact
/// ATTRIBUTION: the association of a line <see cref="Item"/> with one of the
/// application's declared impacts (<see cref="ApplicationImpact"/>). An item has
/// one or more. Mutated through <see cref="Item.AttributeImpacts"/>.
/// </summary>
public class ItemImpact
{
    public int Id { get; private set; }
    public int ItemId { get; private set; }
    public int ApplicationImpactId { get; private set; }

    public ApplicationImpact ApplicationImpact { get; private set; } = null!;

    private ItemImpact() { }

    public ItemImpact(int applicationImpactId)
    {
        ApplicationImpactId = applicationImpactId;
    }
}
