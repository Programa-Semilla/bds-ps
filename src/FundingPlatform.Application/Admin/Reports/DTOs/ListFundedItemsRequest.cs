using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Reports.DTOs;

public sealed class ListFundedItemsRequest
{
    public IReadOnlyList<int> CategoryIds { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> SupplierIds { get; set; } = Array.Empty<int>();
    public IReadOnlyList<ApplicationState> AppStates { get; set; } = Array.Empty<ApplicationState>();
    public DateOnly? ApprovedFrom { get; set; }
    public DateOnly? ApprovedTo { get; set; }
    public bool ExecutedOnly { get; set; }
    /// <summary>Spec 029 / FR-012 — optional Fund filter (null = all Funds).</summary>
    public int? FundId { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string Sort { get; set; } = "approvedAt-desc";
}
