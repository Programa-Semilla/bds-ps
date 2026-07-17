using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Disbursements;

/// <summary>Spec 046 — shared constants for the composed balance. The synthetic default-tranche
/// label lives here (Application layer) so the Infrastructure projection can name the synthetic
/// tranche without depending on Web; <c>TrancheResources</c> (Web) delegates to this value.</summary>
public static class ComposedBalanceDefaults
{
    /// <summary>es-CR label for the virtual default tranche (no DB row) holding unassigned lines.</summary>
    public const string SyntheticTrancheName = "General";
}

/// <summary>
/// Spec 046 — the participant balance decomposed into its tranche → budget-line tree. Each
/// level is a <see cref="ParticipantBalance"/> and equals the sum of its children to the colón
/// (SC-003): line → tranche → participant. The participant node equals the P1 flat balance,
/// whose <c>Allocated</c> reconciles to the <c>DisbursementAllocation.ResolveAsync</c> ledger snapshot.
/// </summary>
public sealed record ComposedBalance(
    ParticipantBalance Participant,
    IReadOnlyList<TrancheBalance> Tranches);

/// <summary>
/// Spec 046 — a tranche (funding phase) rollup. <see cref="TrancheId"/> is <c>null</c> for the
/// synthetic default tranche ("General"), which is present iff ≥1 line is unassigned (research D4).
/// </summary>
public sealed record TrancheBalance(
    int? TrancheId,
    string Name,
    int Ordinal,
    ParticipantBalance Balance,
    IReadOnlyList<BudgetLineBalance> Lines);

/// <summary>
/// Spec 046 — a per-budget-line rollup. <see cref="ParticipantBalance.Allocated"/> is the line
/// budget; <see cref="ParticipantBalance.Committed"/> is the budget if committed else 0; Paid/
/// Validated/Pending come from the line's <c>DisbursementLineAllocation</c> rows; Available may be
/// negative (over-payment, never clamped).
/// </summary>
public sealed record BudgetLineBalance(
    int ItemId,
    string? LineCode,
    string ProductName,
    string? SupplierName,
    ItemCommitState CommitState,
    BudgetLineStatus Status,
    ParticipantBalance Balance,
    ItemClosureState ClosureState = ItemClosureState.Open);

/// <summary>
/// Spec 046 / D3 — the derived (never-stored) budget-line status. A pure function of commit state +
/// attribution sums + disbursement states, computed on read (es-CR labels in <c>TrancheResources</c>).
/// </summary>
public enum BudgetLineStatus
{
    /// <summary><c>CommitState == Uncommitted</c>.</summary>
    Uncommitted,

    /// <summary>Committed, no non-cancelled payment attributed.</summary>
    Committed,

    /// <summary>Committed, Σ non-cancelled attributions &gt; 0 and &lt; line budget.</summary>
    PartiallyPaid,

    /// <summary>Committed, Σ non-cancelled attributions ≥ line budget, not all validated.</summary>
    Paid,

    /// <summary>Committed, all attributions on validated disbursements and Σ ≥ budget.</summary>
    Validated,

    /// <summary>Spec 047 / D3 — the line has been explicitly closed (stored terminal). Takes
    /// precedence over every other rung (a closed line reads as Closed regardless of payment state).</summary>
    Closed,
}
