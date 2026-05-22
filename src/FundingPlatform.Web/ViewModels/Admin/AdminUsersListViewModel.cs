namespace FundingPlatform.Web.ViewModels.Admin;

public class AdminUsersListViewModel
{
    public IReadOnlyList<AdminUserSummaryRowViewModel> Rows { get; init; } = Array.Empty<AdminUserSummaryRowViewModel>();
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? RoleFilter { get; init; }
    public string? StatusFilter { get; init; }
    public string? Search { get; init; }

    /// <summary>Spec 021 / FR-034 / T082 — Process catalog for the cascading
    /// Process → Group group selector. Each process carries its own groups
    /// so the client-side cascade JS can narrow the Group dropdown.</summary>
    public IReadOnlyList<AdminUsersProcessFilterOption> Processes { get; init; }
        = Array.Empty<AdminUsersProcessFilterOption>();

    /// <summary>Spec 021 / FR-034 — currently selected Process filter (null = all).</summary>
    public int? ProcessFilter { get; init; }

    /// <summary>Spec 021 / FR-034 — currently selected Group filter (null = all).</summary>
    public int? GroupFilter { get; init; }

    public int TotalPages =>
        PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

/// <summary>Spec 021 / FR-034 — option-row carrying a Process + its Groups.
/// The view emits each Group as a child option under the Process, and the
/// cascade JS rebuilds the Group dropdown when the Process selection changes.</summary>
public sealed record AdminUsersProcessFilterOption(
    int Id,
    string Name,
    IReadOnlyList<AdminUsersGroupFilterOption> Groups);

/// <summary>Spec 021 / FR-034 — option-row for a Group within a Process.</summary>
public sealed record AdminUsersGroupFilterOption(int Id, string Name);
