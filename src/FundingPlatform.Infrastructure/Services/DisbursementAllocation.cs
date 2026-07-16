// Spec 045 — single source of the allocation-resolution rule (research R1).

using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 045 — the one place the allocation (approved ceiling) is resolved, shared by
/// <see cref="DisbursementService"/> (the reconciliation ceiling / over-disbursement guard)
/// and <see cref="ParticipantBalanceProjection"/> (the <c>Allocated</c> figure the user reads).
/// Both must agree on the highest-stakes number in the feature, so the rule lives here once:
/// the immutable ledger <see cref="LedgerEntryType.Allocation"/> snapshot if present, else the
/// canonical CRC rollup <see cref="ApplicationCurrencyTotal.Compute"/> as a pre-first-disbursement
/// fallback (needs <c>Items → Quotations</c> loaded).
/// </summary>
internal static class DisbursementAllocation
{
    public static async Task<decimal> ResolveAsync(AppDbContext db, int applicationId, CancellationToken ct)
    {
        var ledger = await db.DisbursementLedgerEntries.AsNoTracking()
            .Where(l => l.ApplicationId == applicationId && l.EntryType == LedgerEntryType.Allocation)
            .Select(l => (decimal?)l.Amount)
            .FirstOrDefaultAsync(ct);
        if (ledger is { } snapshot)
        {
            return snapshot;
        }

        var app = await db.Applications.AsNoTracking()
            .Include(a => a.Items).ThenInclude(i => i.Quotations)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        return app is null ? 0m : ApplicationCurrencyTotal.Compute(app).Total ?? 0m;
    }
}
