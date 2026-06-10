using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Reports.DTOs;

public sealed class ListAgingApplicationsRequest
{
    public IReadOnlyList<ApplicationState> States { get; set; } = Array.Empty<ApplicationState>();

    // Property name matches the ?threshold= URL key the Aging form posts so MVC's
    // case-insensitive query binder picks it up. Days is implicit in the column label.
    public int Threshold { get; set; } = 14;

    public string? Search { get; set; }
    /// <summary>Spec 029 / FR-012 — optional Fund filter (null = all Funds).</summary>
    public int? FundId { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string Sort { get; set; } = "days-desc";
}
