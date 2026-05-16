namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 016 / 021 — view model for the admin Groups list (FR-003).</summary>
public sealed class AdminGroupsIndexViewModel
{
    public IReadOnlyList<AdminGroupRow> Groups { get; init; } = Array.Empty<AdminGroupRow>();
}

/// <summary>Spec 021 / FR-001 — a Groups-index row carries the owning Process
/// name so the catalog surfaces which Process each Group belongs to.</summary>
public sealed record AdminGroupRow(int Id, string Name, int MemberCount, string ProcessName);
