using FundingPlatform.Application.Admin.Filters;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels.Admin;

public class AdminSupplierListViewModel
{
    public IReadOnlyList<AdminSupplierRowViewModel> Items { get; init; } = Array.Empty<AdminSupplierRowViewModel>();
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public SupplierVerificationStatus? StatusFilter { get; init; }
    public string? LegalIdFilter { get; init; }
    public string? NameFilter { get; init; }
    public bool HasIncompleteCompliance { get; init; }

    /// <summary>
    /// Spec 021 / US3 / T108 / FR-009 — single supplier-admin search term,
    /// applied to both <c>Name</c> and <c>CédulaJurídica</c>.
    /// </summary>
    public string? SearchTerm { get; init; }

    /// <summary>
    /// Spec 021 / US3 / T108 / FR-011 — Process filter on the supplier-admin
    /// list (null = "all processes").
    /// </summary>
    public int? ProcessIdFilter { get; init; }

    /// <summary>Currently selected Fund filter (null = all).</summary>
    public int? FundFilter { get; init; }

    /// <summary>Fondo → Proceso catalog (active Funds only) for the cascading
    /// drill-down filter. Suppliers are not Group-scoped, so the Group level is
    /// not rendered here.</summary>
    public IReadOnlyList<FundHierarchyNode> FundHierarchy { get; init; }
        = Array.Empty<FundHierarchyNode>();

    public int TotalPages =>
        PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public record AdminSupplierRowViewModel(
    int Id,
    string LegalId,
    string Name,
    SupplierVerificationStatus Status,
    int BranchCount,
    bool HasIncompleteCompliance,
    DateTime UpdatedAt,
    DateTime? LastUsedAt = null);
