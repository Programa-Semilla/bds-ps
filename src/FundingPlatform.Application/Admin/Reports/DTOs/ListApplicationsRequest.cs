using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Admin.Reports.DTOs;

public sealed class ListApplicationsRequest
{
    public IReadOnlyList<ApplicationState> States { get; set; } = Array.Empty<ApplicationState>();
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    public bool? HasAgreement { get; set; }
    public bool? HasActiveAppeal { get; set; }
    /// <summary>Spec 029 / FR-012 — optional Fund filter (null = all Funds).</summary>
    public int? FundId { get; set; }
    /// <summary>Optional Process filter via the Group anchor (null = all).</summary>
    public int? ProcessId { get; set; }
    /// <summary>Optional Group filter via the Group anchor (null = all).</summary>
    public int? GroupId { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string Sort { get; set; } = "updated-desc";
}
