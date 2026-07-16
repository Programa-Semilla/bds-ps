// Spec 045/046 — see specs/046-tranches-budget-lines/data-model.md (Balance projection — composed tree).

using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 045 / FR-019 — projects the participant balance from the append-only ledger plus mutable
/// pending disbursements. Spec 046 adds the sixth dimension <c>Committed</c> (Σ committed line
/// budgets) and the composed tranche → budget-line tree. No stored balance column (FR-017);
/// <c>Available = Allocated − Paid</c> is never clamped (FR-020). Each composed level equals the
/// sum of its children to the colón (SC-003).
/// </summary>
public sealed class ParticipantBalanceProjection : IParticipantBalanceProjection
{
    private readonly AppDbContext _db;

    public ParticipantBalanceProjection(AppDbContext db)
    {
        _db = db;
    }

    // ---------------------------------------------------------------- flat (P1, now 6-dim)

    public async Task<ParticipantBalance> GetForApplicationAsync(int applicationId, CancellationToken ct)
    {
        // Allocated / Validated / Pending are unchanged from P1 (SC-006 — legacy balances stay put).
        var allocated = await DisbursementAllocation.ResolveAsync(_db, applicationId, ct);

        var validated = await _db.DisbursementLedgerEntries.AsNoTracking()
            .Where(l => l.ApplicationId == applicationId && l.EntryType == LedgerEntryType.Disbursement)
            .SumAsync(l => (decimal?)l.Amount, ct) ?? 0m;

        var pending = await _db.Disbursements.AsNoTracking()
            .Where(d => d.ApplicationId == applicationId
                        && (d.State == DisbursementState.Recorded || d.State == DisbursementState.Inconsistent))
            .SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;

        // Spec 046 — Committed = Σ budgets of committed budget-lines (display-only; does not change Available).
        var committed = await CommittedTotalAsync(applicationId, ct);

        var paid = validated + pending;
        var available = allocated - paid; // may be negative (over-disbursement signal, FR-020)

        return new ParticipantBalance(allocated, committed, paid, validated, pending, available);
    }

    private async Task<decimal> CommittedTotalAsync(int applicationId, CancellationToken ct)
    {
        // LineBudget LINQ twin (ApplicationCurrencyTotal.LineBudget): selected non-legacy quote's
        // converted CRC amount, summed over committed lines only.
        var budgets = await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && i.CommitState == ItemCommitState.Committed)
            .Select(i => i.Quotations
                .Where(q => q.SupplierId == i.SelectedSupplierId && !q.LegacyNeedsReview && q.ConvertedCrcAmount != null)
                .Select(q => (decimal?)q.ConvertedCrcAmount)
                .FirstOrDefault() ?? 0m)
            .ToListAsync(ct);
        return budgets.Sum();
    }

    // ---------------------------------------------------------------- composed tree (P2)

    public async Task<ComposedBalance> GetComposedForApplicationAsync(
        int applicationId, BudgetLineFilter? filter, CancellationToken ct)
    {
        // 1) Per-line: budget (LineBudget twin), commit state, tranche membership, supplier.
        var lineRows = await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId)
            .Select(i => new
            {
                i.Id,
                i.LineCode,
                i.ProductName,
                i.TrancheId,
                i.CommitState,
                i.SelectedSupplierId,
                SupplierName = i.SelectedSupplier != null ? i.SelectedSupplier.Name : null,
                Budget = i.Quotations
                    .Where(q => q.SupplierId == i.SelectedSupplierId && !q.LegacyNeedsReview && q.ConvertedCrcAmount != null)
                    .Select(q => (decimal?)q.ConvertedCrcAmount)
                    .FirstOrDefault() ?? 0m,
            })
            .ToListAsync(ct);

        // 2) Per-line non-cancelled attributions joined to their disbursement state + date.
        var allocRows = await _db.DisbursementLineAllocations.AsNoTracking()
            .Where(a => _db.Items.Any(i => i.Id == a.ItemId && i.ApplicationId == applicationId))
            .Join(_db.Disbursements.AsNoTracking(),
                a => a.DisbursementId, d => d.Id,
                (a, d) => new { a.ItemId, a.Amount, d.State, d.PaymentDate })
            .Where(x => x.State != DisbursementState.Cancelled)
            .ToListAsync(ct);

        var allocsByItem = allocRows
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 3) Tranche metadata (ordered).
        var tranches = await _db.Tranches.AsNoTracking()
            .Where(t => t.ApplicationId == applicationId)
            .OrderBy(t => t.Ordinal).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.Name, t.Ordinal })
            .ToListAsync(ct);

        // 4) Build each budget-line node (DTO + the fields the filter/grouping need).
        var lines = lineRows.Select(r =>
        {
            var attributions = allocsByItem.TryGetValue(r.Id, out var list) ? list : null;
            var validated = attributions?.Where(x => x.State == DisbursementState.Validated).Sum(x => x.Amount) ?? 0m;
            var pending = attributions?
                .Where(x => x.State == DisbursementState.Recorded || x.State == DisbursementState.Inconsistent)
                .Sum(x => x.Amount) ?? 0m;
            IReadOnlyList<DateOnly> paymentDates = attributions?.Select(x => x.PaymentDate).ToList() ?? [];

            var balance = LineBalance(r.Budget, r.CommitState, validated, pending);
            var status = DeriveStatus(r.CommitState, r.Budget, balance.Paid, pending);

            return new LineNode(
                r.TrancheId, r.SelectedSupplierId, paymentDates, status, balance,
                new BudgetLineBalance(r.Id, r.LineCode, r.ProductName, r.SupplierName, r.CommitState, status, balance));
        }).ToList();

        // 5) Apply US4 filters (FR-020) at the line level.
        var filtered = filter is null
            ? lines
            : lines.Where(l =>
                MatchesTranche(l.TrancheId, filter)
                && (filter.Status is null || l.Status == filter.Status)
                && (filter.SupplierId is null || l.SelectedSupplierId == filter.SupplierId)
                && MatchesValidationState(l.Status, l.Balance.PendingValidation, filter.ValidationState)
                && MatchesDate(l.PaymentDates, filter.PaymentDateFrom, filter.PaymentDateTo)).ToList();

        // 6) Group into tranches (explicit, ordered) + a synthetic "General" for unassigned lines.
        var trancheBalances = new List<TrancheBalance>();
        foreach (var t in tranches)
        {
            var tLines = filtered.Where(l => l.TrancheId == t.Id).ToList();
            if (tLines.Count == 0)
            {
                continue; // drop tranches with no matching lines (only relevant under a filter)
            }
            trancheBalances.Add(new TrancheBalance(
                t.Id, t.Name, t.Ordinal,
                SumBalances(tLines.Select(l => l.Balance)),
                tLines.Select(l => l.Dto).ToList()));
        }

        var syntheticLines = filtered.Where(l => l.TrancheId is null).ToList();
        if (syntheticLines.Count > 0)
        {
            trancheBalances.Add(new TrancheBalance(
                null, ComposedBalanceDefaults.SyntheticTrancheName, int.MaxValue,
                SumBalances(syntheticLines.Select(l => l.Balance)),
                syntheticLines.Select(l => l.Dto).ToList()));
        }

        var participant = SumBalances(filtered.Select(l => l.Balance));
        return new ComposedBalance(participant, trancheBalances);
    }

    /// <summary>Intermediate per-line node: the DTO plus the fields grouping/filtering read.</summary>
    private sealed record LineNode(
        int? TrancheId,
        int? SelectedSupplierId,
        IReadOnlyList<DateOnly> PaymentDates,
        BudgetLineStatus Status,
        ParticipantBalance Balance,
        BudgetLineBalance Dto);

    // ---------------------------------------------------------------- pure helpers

    private const decimal Tolerance = 0.01m;

    private static ParticipantBalance LineBalance(decimal budget, ItemCommitState commit, decimal validated, decimal pending)
    {
        var paid = validated + pending;
        return new ParticipantBalance(
            Allocated: budget,
            Committed: commit == ItemCommitState.Committed ? budget : 0m,
            Paid: paid,
            Validated: validated,
            PendingValidation: pending,
            Available: budget - paid);
    }

    private static ParticipantBalance SumBalances(IEnumerable<ParticipantBalance> children)
    {
        decimal alloc = 0, committed = 0, paid = 0, validated = 0, pending = 0, available = 0;
        foreach (var b in children)
        {
            alloc += b.Allocated;
            committed += b.Committed;
            paid += b.Paid;
            validated += b.Validated;
            pending += b.PendingValidation;
            available += b.Available;
        }
        return new ParticipantBalance(alloc, committed, paid, validated, pending, available);
    }

    /// <summary>Spec 046 / D3 — derive the budget-line status from commit + attribution sums.</summary>
    private static BudgetLineStatus DeriveStatus(ItemCommitState commit, decimal budget, decimal paid, decimal pending)
    {
        if (commit == ItemCommitState.Uncommitted)
        {
            return BudgetLineStatus.Uncommitted;
        }
        if (paid < Tolerance)
        {
            return BudgetLineStatus.Committed;
        }
        if (paid + Tolerance <= budget)
        {
            return BudgetLineStatus.PartiallyPaid; // 0 < Σ < budget
        }
        // Σ ≥ budget.
        return pending < Tolerance ? BudgetLineStatus.Validated : BudgetLineStatus.Paid;
    }

    private static bool MatchesTranche(int? lineTrancheId, BudgetLineFilter filter)
    {
        if (filter.TrancheId is { } tid)
        {
            return lineTrancheId == tid;
        }
        if (filter.IncludeSyntheticTranche)
        {
            return lineTrancheId is null;
        }
        return true;
    }

    private static bool MatchesValidationState(BudgetLineStatus status, decimal pending, BudgetLineValidationState? state) => state switch
    {
        null => true,
        BudgetLineValidationState.HasPending => pending >= Tolerance,
        BudgetLineValidationState.FullyValidated => status == BudgetLineStatus.Validated,
        _ => true,
    };

    private static bool MatchesDate(IReadOnlyList<DateOnly> dates, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null)
        {
            return true;
        }
        return dates.Any(d => (from is null || d >= from) && (to is null || d <= to));
    }
}
