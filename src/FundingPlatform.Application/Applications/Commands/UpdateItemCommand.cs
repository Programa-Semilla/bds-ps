namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 035 / US2 — mirrors <see cref="AddItemCommand"/> for an existing item.
/// Changing the category clears the previous category's values (Item.ChangeCategory).
/// </summary>
public record UpdateItemCommand(
    int ItemId,
    int ApplicationId,
    string ProductName,
    int CategoryId,
    Dictionary<int, string?> CategoryFieldValues,
    IReadOnlyList<int> ApplicationImpactIds,
    string? ImpactJustification);
