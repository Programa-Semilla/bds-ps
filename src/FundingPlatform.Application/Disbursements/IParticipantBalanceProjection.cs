using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Disbursements;

/// <summary>
/// Spec 045 / FR-019 — projects the participant balance for an executed application from the
/// append-only ledger plus mutable pending disbursements (research R3). Read-only; used by the
/// operator write surface and the Auditor/Admin read surface. Spec 046 adds the composed
/// tranche → budget-line tree.
/// </summary>
public interface IParticipantBalanceProjection
{
    /// <summary>The flat participant balance (spec 046: now 6-dimension including <c>Committed</c>).</summary>
    Task<ParticipantBalance> GetForApplicationAsync(int applicationId, CancellationToken ct);

    /// <summary>Spec 046 — the balance decomposed into the tranche → budget-line tree. Optional filters
    /// (US4, FR-020) narrow the returned budget-lines; a synthetic "General" tranche appears iff any
    /// line is unassigned.</summary>
    Task<ComposedBalance> GetComposedForApplicationAsync(
        int applicationId, BudgetLineFilter? filter, CancellationToken ct);
}

/// <summary>
/// Spec 046 / FR-020 (US4) — optional budget-line filters for the composed projection: by tranche,
/// derived status, selected supplier, validation state, and payment date. All null ⇒ no filtering.
/// </summary>
public sealed record BudgetLineFilter(
    int? TrancheId = null,
    bool IncludeSyntheticTranche = false,
    BudgetLineStatus? Status = null,
    int? SupplierId = null,
    BudgetLineValidationState? ValidationState = null,
    DateOnly? PaymentDateFrom = null,
    DateOnly? PaymentDateTo = null);

/// <summary>Spec 046 / D3 — the separate "validation state" filter facet (distinct from status).</summary>
public enum BudgetLineValidationState
{
    /// <summary>The line has at least one attribution on a not-yet-validated (pending) disbursement.</summary>
    HasPending,

    /// <summary>Every attribution on the line is on a validated disbursement (and Σ ≥ budget).</summary>
    FullyValidated,
}
