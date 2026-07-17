using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.DocRules;

/// <summary>
/// Spec 047 / FR-013 — the per-budget-line required-document completeness for one application.
/// <see cref="Present"/> is the union of BOTH sources (research D1): the graph <c>Evidence.Type</c>s
/// linked to the line AND the <c>DisbursementEvidence.Kind</c>s carried by validated disbursements
/// that paid the line (bank receipt / invoice only). <see cref="Missing"/> = required − present.
/// </summary>
public sealed record LineCompleteness(
    int ItemId,
    IReadOnlyCollection<EvidenceType> Required,
    IReadOnlyCollection<EvidenceType> Present,
    IReadOnlyCollection<EvidenceType> Missing)
{
    public bool IsComplete => Missing.Count == 0;
    public bool EvidenceIncomplete => Missing.Count > 0;
}

/// <summary>
/// Spec 047 — computes <see cref="LineCompleteness"/> for every budget-line of an application in one
/// batched read (avoids the N+1 across lines, FR/NFR). Used by the evidence/disbursement completeness
/// matrix (US2) and the budget-line closure gate (US3).
/// </summary>
public interface ILineCompletenessProjection
{
    Task<IReadOnlyDictionary<int, LineCompleteness>> GetForApplicationAsync(int applicationId, CancellationToken ct);
}
