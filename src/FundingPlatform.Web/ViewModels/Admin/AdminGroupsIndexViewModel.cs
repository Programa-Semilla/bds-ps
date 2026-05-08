namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 016 — view model for the admin Groups list (FR-003).</summary>
public sealed class AdminGroupsIndexViewModel
{
    public IReadOnlyList<AdminGroupRow> Groups { get; init; } = Array.Empty<AdminGroupRow>();
}

public sealed record AdminGroupRow(int Id, string Name, int MemberCount);
