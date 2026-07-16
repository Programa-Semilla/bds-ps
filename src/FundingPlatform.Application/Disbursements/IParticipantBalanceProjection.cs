using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Disbursements;

/// <summary>
/// Spec 045 / FR-019 — projects the five-dimension participant balance for an executed
/// application from the append-only ledger plus mutable pending disbursements (research R3).
/// Read-only; used by the operator write surface and the Auditor/Admin read surface.
/// </summary>
public interface IParticipantBalanceProjection
{
    Task<ParticipantBalance> GetForApplicationAsync(int applicationId, CancellationToken ct);
}
