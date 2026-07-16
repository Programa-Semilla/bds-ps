using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Web.ViewModels.Disbursements;

/// <summary>Spec 045 — the per-application disbursement surface: five-dimension balance,
/// the record form, and the disbursement list. <see cref="CanWrite"/> is false for the
/// Auditor read-only view (controls hidden — FR-025).</summary>
public sealed class DisbursementIndexViewModel
{
    public required int ApplicationId { get; init; }
    public required ParticipantBalance Balance { get; init; }
    public required IReadOnlyList<DisbursementListItem> Items { get; init; }
    public required bool CanWrite { get; init; }

    /// <summary>Spec 046 — the composed tranche → budget-line tree (with any US4 filters applied).</summary>
    public ComposedBalance? Composed { get; init; }

    /// <summary>Spec 046 / US4 — the active budget-line filter (echoed back into the toolbar).</summary>
    public BudgetLineFilterForm Filter { get; init; } = new();

    /// <summary>Spec 046 / US4 — supplier options for the filter (id → display name), from the app's lines.</summary>
    public IReadOnlyList<(int Id, string Name)> SupplierOptions { get; init; } = [];

    /// <summary>Spec 046 / US4 — the full tranche list (id → name) for the filter dropdown, independent
    /// of the active filter (so filtering by tranche doesn't hide the other options).</summary>
    public IReadOnlyList<(int Id, string Name)> TrancheOptions { get; init; } = [];
}

/// <summary>Spec 046 / US4 — the raw filter inputs bound from the query string (all optional).</summary>
public sealed class BudgetLineFilterForm
{
    public int? TrancheId { get; init; }
    public bool Synthetic { get; init; }
    public string? Status { get; init; }
    public int? SupplierId { get; init; }
    public string? ValidationState { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }

    public bool IsActive =>
        TrancheId is not null || Synthetic || !string.IsNullOrEmpty(Status)
        || SupplierId is not null || !string.IsNullOrEmpty(ValidationState)
        || DateFrom is not null || DateTo is not null;
}

/// <summary>Spec 045 — the disbursement detail: amounts, typed evidence, the live
/// discrepancy list, and the lifecycle actions. Write controls gated by <see cref="CanWrite"/>.</summary>
public sealed class DisbursementDetailViewModel
{
    public required int ApplicationId { get; init; }
    public required DisbursementDetail Detail { get; init; }
    public required bool CanWrite { get; init; }
    public required string AcceptExtensions { get; init; }

    /// <summary>Spec 046 / US3 — committed budget-lines available for the Edit split editor.</summary>
    public IReadOnlyList<BudgetLineBalance> CommittedLines { get; init; } = [];
}

/// <summary>Spec 045 — the group-scoped disbursement inbox (executed applications in
/// active processes). Reuses the spec-041 evidence-inbox projection.</summary>
public sealed class DisbursementInboxViewModel
{
    public required IReadOnlyList<DisbursementInboxRowViewModel> Rows { get; init; }
}

public sealed class DisbursementInboxRowViewModel
{
    public required int ApplicationId { get; init; }
    public required string ApplicationNumber { get; init; }
    public required string ApplicantName { get; init; }
    public required string FundName { get; init; }
    public required string ProcessName { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }
}
