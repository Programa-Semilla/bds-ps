using FundingPlatform.Application.Admin.Filters;

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

    /// <summary>Fondo → Proceso → Grupo catalog (active Funds only) for the
    /// cascading drill-down filter.</summary>
    public IReadOnlyList<FundHierarchyNode> FundHierarchy { get; init; }
        = Array.Empty<FundHierarchyNode>();

    /// <summary>Currently selected Fund filter (null = all).</summary>
    public int? FundFilter { get; init; }

    /// <summary>Currently selected Process filter (null = all).</summary>
    public int? ProcessFilter { get; init; }

    /// <summary>Currently selected Group filter (null = all).</summary>
    public int? GroupFilter { get; init; }

    public int TotalPages =>
        PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
