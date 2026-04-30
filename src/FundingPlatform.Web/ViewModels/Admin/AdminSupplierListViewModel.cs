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
    DateTime UpdatedAt);
