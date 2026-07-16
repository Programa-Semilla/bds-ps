namespace FundingPlatform.Application.Tranches;

/// <summary>Spec 046 — a tranche as shown on the reviewer editor: its derived amount
/// (Σ member-line budgets) and the ids of the lines assigned to it.</summary>
public sealed record TrancheView(
    int Id,
    string Name,
    int Ordinal,
    decimal DerivedAmount,
    IReadOnlyList<int> ItemIds);

/// <summary>Spec 046 — a budget-line row on the reviewer tranche editor: its budget and its
/// current tranche membership (<c>null</c> = synthetic default "General").</summary>
public sealed record TrancheEditorLine(
    int ItemId,
    string? LineCode,
    string ProductName,
    decimal Budget,
    int? TrancheId);
