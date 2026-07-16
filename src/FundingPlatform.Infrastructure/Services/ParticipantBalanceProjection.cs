// Spec 045 — see specs/045-financial-disbursement-core/data-model.md (Balance projection) and research R3.

using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 045 / FR-019 — projects the five-dimension participant balance from the
/// append-only ledger plus mutable pending disbursements. No stored balance column
/// (FR-017); a validated disbursement contributes only via its ledger entry (its
/// <c>Disbursements</c> row is <c>Validated</c> and excluded from the Pending sum), so no
/// double-count (FR-021). <c>Available = Allocated − Paid</c> is never clamped (FR-020).
/// </summary>
public sealed class ParticipantBalanceProjection : IParticipantBalanceProjection
{
    private readonly AppDbContext _db;

    public ParticipantBalanceProjection(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ParticipantBalance> GetForApplicationAsync(int applicationId, CancellationToken ct)
    {
        // Allocated: the ledger Allocation snapshot if present, else the canonical CRC rollup
        // (research R1). Shared with DisbursementService via DisbursementAllocation so the figure
        // the user reads can never drift from the ceiling reconciliation enforces.
        var allocated = await DisbursementAllocation.ResolveAsync(_db, applicationId, ct);

        // Validated: Σ ledger Disbursement entries.
        var validated = await _db.DisbursementLedgerEntries.AsNoTracking()
            .Where(l => l.ApplicationId == applicationId && l.EntryType == LedgerEntryType.Disbursement)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0m;

        // Pending: Σ mutable off-ledger disbursements (Recorded/Inconsistent).
        var pending = await _db.Disbursements.AsNoTracking()
            .Where(d => d.ApplicationId == applicationId
                        && (d.State == DisbursementState.Recorded || d.State == DisbursementState.Inconsistent))
            .SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;

        var paid = validated + pending;
        var available = allocated - paid; // may be negative (over-disbursement signal, FR-020)

        return new ParticipantBalance(allocated, paid, validated, pending, available);
    }
}
