using FundingPlatform.Application.Reconciliation;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels.Reconciliation;

/// <summary>Spec 048 / US3 — the dashboard index: summary tiles + filter form + list.</summary>
public sealed class ReconciliationIndexViewModel
{
    public required ReconciliationSummaryDto Summary { get; init; }
    public required IReadOnlyList<DiscrepancyRowDto> Rows { get; init; }
    public required ReconciliationFilterForm Filter { get; init; }
    public required IReadOnlyList<(int Id, string Name)> SupplierOptions { get; init; }
    public required IReadOnlyList<(int Id, string Name)> TrancheOptions { get; init; }
    public required IReadOnlyList<(string UserId, string Name)> ResponsibleOptions { get; init; }
    public bool CanWrite { get; init; }
}

/// <summary>Spec 048 — the raw filter form bound from the GET query string.</summary>
public sealed class ReconciliationFilterForm
{
    public DiscrepancySeverity? Severity { get; init; }
    public DiscrepancyState? State { get; init; }
    public int? SupplierId { get; init; }
    public int? TrancheId { get; init; }
    public int? ParticipantApplicationId { get; init; }
    public string? ResponsibleUserId { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public bool OpenOnly { get; init; } = true;

    public ReconciliationFilter ToFilter() => new(
        ParticipantApplicationId: ParticipantApplicationId,
        TrancheId: TrancheId,
        ItemId: null,
        SupplierId: SupplierId,
        DateFrom: DateFrom,
        DateTo: DateTo,
        Severity: Severity,
        State: State,
        ResponsibleUserId: ResponsibleUserId,
        OpenOnly: State is null && OpenOnly);
}

/// <summary>Spec 048 / US3 — the discrepancy detail: row + timeline + write affordances.</summary>
public sealed class ReconciliationDetailViewModel
{
    public required DiscrepancyDetailDto Detail { get; init; }
    public required IReadOnlyList<(string UserId, string Name)> AssigneeOptions { get; init; }
    public bool CanWrite { get; init; }
}
