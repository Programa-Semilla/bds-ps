// Spec 048 — see specs/048-full-reconciliation-engine/contracts/interfaces.md (materializer contract).

using FundingPlatform.Application.Reconciliation;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Services;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 048 — implements <see cref="IReconciliationMaterializer"/>. Wraps the existing pure blocking
/// evaluators (<see cref="DisbursementReconciliation"/> / <see cref="DisbursementLineReconciliation"/>)
/// and the new <see cref="ReconciliationWarnings"/>, recomputing the application's full current
/// discrepancy set from live data with a handful of batched reads (no N+1 — the P3 completeness-projection
/// lesson), then reconciling it against the persisted <see cref="Discrepancy"/> rows by stable identity.
/// Best-effort and non-throwing: it is the visibility snapshot, never the money guarantee (the gates keep
/// recomputing fresh and throwing — FR-004 / SC-004). A failure to persist the snapshot logs and returns
/// so it can never fail the user's already-committed mutation.
/// </summary>
public sealed class ReconciliationMaterializer : IReconciliationMaterializer
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReconciliationMaterializer> _logger;

    public ReconciliationMaterializer(AppDbContext db, ILogger<ReconciliationMaterializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task MaterializeAsync(int applicationId, string actorUserId, CancellationToken ct)
    {
        try
        {
            await MaterializeCoreAsync(applicationId, ct);
        }
        catch (Exception ex)
        {
            // The triggering mutation already committed. The snapshot is for visibility only, so a
            // persistence failure here must never surface to the caller (FR-004 — the money gate is
            // authoritative and independent). The next mutation re-runs materialization and self-heals.
            _logger.LogError(ex, "Reconciliation materialization for application {ApplicationId} failed; the visibility snapshot may be stale until the next mutation.", applicationId);
        }
    }

    private async Task MaterializeCoreAsync(int applicationId, CancellationToken ct)
    {
        // The auto transitions (Detect/AutoResolve/AutoReopen) carry FKs to AspNetUsers, so the
        // automated actor must be a real id — the system sentinel (excluded by a global query filter).
        var systemActorId = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsSystemSentinel)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(systemActorId))
        {
            _logger.LogError("Reconciliation materialization for application {ApplicationId} skipped: no system sentinel user found.", applicationId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var current = await ComputeCurrentSetAsync(applicationId, systemActorId, ct);

        var persisted = await _db.Discrepancies
            .Include(d => d.Events)
            .Where(d => d.ApplicationId == applicationId)
            .ToListAsync(ct);
        // First-wins dedup rather than ToDictionary: the UX_Discrepancies_Identity unique index makes a
        // duplicate identity impossible on real SQL, but if one ever slipped through (a swallowed
        // best-effort insert race), ToDictionary would throw and the snapshot would never self-heal.
        var persistedByIdentity = new Dictionary<(DiscrepancyScopeType, int, ReconciliationComparison), Discrepancy>();
        foreach (var p in persisted)
        {
            persistedByIdentity.TryAdd((p.ScopeType, p.ScopeEntityId, p.Comparison), p);
        }

        // Present discrepancies: refresh in place, or detect anew.
        foreach (var (identity, c) in current)
        {
            if (persistedByIdentity.TryGetValue(identity, out var row))
            {
                if (row.State == DiscrepancyState.Resolved)
                {
                    row.AutoReopen(systemActorId, now); // recurrence
                }
                row.Refresh(c.Expected, c.Actual, systemActorId, now);
            }
            else
            {
                _db.Discrepancies.Add(Discrepancy.Detect(
                    applicationId, c.ScopeType, c.ScopeEntityId, c.Comparison, c.Severity,
                    c.Expected, c.Actual, toleranceApplied: 0m, c.SourceDocument, systemActorId, now));
            }
        }

        // Cleared discrepancies: a persisted non-terminal row whose identity is no longer present.
        foreach (var row in persisted)
        {
            if (!current.ContainsKey((row.ScopeType, row.ScopeEntityId, row.Comparison)))
            {
                row.AutoResolve(systemActorId, now); // idempotent no-op on terminal rows
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- current-set computation

    private readonly record struct Identity(DiscrepancyScopeType ScopeType, int ScopeEntityId, ReconciliationComparison Comparison);

    private sealed record Computed(
        DiscrepancyScopeType ScopeType, int ScopeEntityId, ReconciliationComparison Comparison,
        DiscrepancySeverity Severity, decimal Expected, decimal Actual, string SourceDocument);

    private async Task<Dictionary<(DiscrepancyScopeType, int, ReconciliationComparison), Computed>> ComputeCurrentSetAsync(
        int applicationId, string systemActorId, CancellationToken ct)
    {
        var set = new Dictionary<(DiscrepancyScopeType, int, ReconciliationComparison), Computed>();
        void Emit(Computed c) => set[(c.ScopeType, c.ScopeEntityId, c.Comparison)] = c;

        // ---- batched reads (each a single query) -------------------------------------------------
        var allocation = await DisbursementAllocation.ResolveAsync(_db, applicationId, ct);

        var disbursements = await _db.Disbursements.AsNoTracking()
            .Where(d => d.ApplicationId == applicationId && d.State != DisbursementState.Cancelled)
            .Select(d => new { d.Id, d.Amount, d.PaymentDate, d.State })
            .ToListAsync(ct);

        var disbursementIds = disbursements.Select(d => d.Id).ToList();

        var evidenceAmounts = await _db.DisbursementEvidence.AsNoTracking()
            .Where(e => disbursementIds.Contains(e.DisbursementId))
            .Select(e => new { e.DisbursementId, e.Kind, e.Amount })
            .ToListAsync(ct);

        var splits = await _db.DisbursementLineAllocations.AsNoTracking()
            .Where(a => disbursementIds.Contains(a.DisbursementId))
            .Select(a => new { a.DisbursementId, a.ItemId, a.Amount })
            .ToListAsync(ct);

        var committedLines = await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && i.CommitState == ItemCommitState.Committed)
            .Select(i => new
            {
                i.Id,
                i.LineCode,
                Budget = i.Quotations
                    .Where(q => q.SupplierId == i.SelectedSupplierId && !q.LegacyNeedsReview && q.ConvertedCrcAmount != null)
                    .Select(q => (decimal?)q.ConvertedCrcAmount)
                    .FirstOrDefault() ?? 0m,
            })
            .ToListAsync(ct);

        // Spec 047 evidence-graph nodes for the app (date-anomaly + supplier resolution + drift).
        var graphEvidence = await _db.Evidence.AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .Select(e => new { e.Id, e.Type, e.Amount, e.DocumentDate, e.DisbursementId, e.SupplierId })
            .ToListAsync(ct);

        var graphInvoiceAllocations = await _db.EvidenceLineAllocations.AsNoTracking()
            .Where(a => _db.Evidence.Any(e => e.Id == a.EvidenceId
                && e.ApplicationId == applicationId && e.Type == EvidenceType.Invoice))
            .Select(a => new { a.ItemId, a.Amount })
            .ToListAsync(ct);

        var executionDate = await _db.FundingAgreements.AsNoTracking()
            .Where(fa => fa.ApplicationId == applicationId)
            .Select(fa => (DateTime?)fa.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);

        // ---- blocking legs (reuse the existing pure evaluators) ----------------------------------
        var sumNonCancelled = disbursements.Sum(d => d.Amount);

        foreach (var d in disbursements)
        {
            decimal? bank = evidenceAmounts.Where(e => e.DisbursementId == d.Id && e.Kind == EvidenceKind.BankReceipt)
                .Select(e => (decimal?)e.Amount).FirstOrDefault();
            decimal? invoice = evidenceAmounts.Where(e => e.DisbursementId == d.Id && e.Kind == EvidenceKind.Invoice)
                .Select(e => (decimal?)e.Amount).FirstOrDefault();

            foreach (var disc in DisbursementReconciliation.Evaluate(d.Amount, bank, invoice, sumNonCancelled, allocation))
            {
                // Comparison (c) is application-level — every disbursement produces the same identity,
                // which the dictionary collapses to one Participant-scoped row.
                var (scope, entityId) = disc.Comparison == ReconciliationComparison.TotalVsAllocation
                    ? (DiscrepancyScopeType.Participant, applicationId)
                    : (DiscrepancyScopeType.Payment, d.Id);
                Emit(new Computed(scope, entityId, disc.Comparison, DiscrepancySeverity.Blocking,
                    disc.Expected, disc.Actual, disc.SourceDocument));
            }

            // Split integrity (comparison 3) — only when this disbursement carries a per-line split.
            var lines = splits.Where(s => s.DisbursementId == d.Id)
                .Select(s => (s.ItemId, s.Amount)).ToList();
            if (lines.Count > 0)
            {
                foreach (var split in DisbursementLineReconciliation.EvaluateSplit(d.Amount, lines))
                {
                    Emit(new Computed(DiscrepancyScopeType.Payment, d.Id, split.Comparison,
                        DiscrepancySeverity.Blocking, split.Expected, split.Actual, split.SourceDocument));
                }
            }
        }

        // Per-line over-payment (comparison 4) — Σ non-cancelled payments vs committed budget.
        var paidByLine = splits.GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var overpaymentInputs = committedLines
            .Select(l => new LinePaymentVsBudget(l.Id, l.LineCode ?? $"L-{l.Id}", l.Budget,
                paidByLine.TryGetValue(l.Id, out var paid) ? paid : 0m))
            .ToList();
        foreach (var over in DisbursementLineReconciliation.EvaluateLineOverpayments(overpaymentInputs))
        {
            Emit(new Computed(DiscrepancyScopeType.BudgetLine, over.ItemId,
                ReconciliationComparison.LinePaymentVsBudget, DiscrepancySeverity.Blocking,
                over.Committed, over.Paid, DisbursementLineReconciliation.SourceLineSplit));
        }

        // ---- warning legs (ReconciliationWarnings) -----------------------------------------------
        // (a) Evidence date anomalies — graph documents dated after their payment or before execution.
        var paymentDateById = disbursements.ToDictionary(d => d.Id, d => d.PaymentDate);
        var execDate = executionDate is { } ex ? DateOnly.FromDateTime(ex) : DateOnly.MinValue;
        var dateInputs = graphEvidence
            .Select(e => new EvidenceDateInput(
                e.Id, e.Amount, e.DocumentDate,
                e.DisbursementId is { } did && paymentDateById.TryGetValue(did, out var pd) ? pd : null))
            .ToList();
        foreach (var w in ReconciliationWarnings.EvaluateEvidenceDateAnomalies(dateInputs, execDate))
        {
            Emit(new Computed(w.ScopeType, w.ScopeEntityId, w.Comparison, DiscrepancySeverity.Warning,
                w.Expected, w.Actual, w.SourceDocument));
        }

        // (b) Possible duplicate payments — same supplier + amount + date across non-cancelled payments.
        // Resolve each payment's supplier from its graph invoice; payments with no known supplier abstain.
        var invoiceSupplierByDisbursement = graphEvidence
            .Where(e => e.Type == EvidenceType.Invoice && e.DisbursementId != null && e.SupplierId != null)
            .GroupBy(e => e.DisbursementId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.SupplierId!.Value).First());
        var fingerprints = disbursements
            .Where(d => invoiceSupplierByDisbursement.ContainsKey(d.Id))
            .Select(d => new PaymentFingerprint(d.Id, invoiceSupplierByDisbursement[d.Id], d.Amount, d.PaymentDate))
            .ToList();
        foreach (var w in ReconciliationWarnings.EvaluatePossibleDuplicatePayments(fingerprints))
        {
            Emit(new Computed(w.ScopeType, w.ScopeEntityId, w.Comparison, DiscrepancySeverity.Warning,
                w.Expected, w.Actual, w.SourceDocument));
        }

        // (c) Graph-invoice allocation drift — validated line payments vs Σ graph-invoice allocations.
        var validatedDisbursementIds = disbursements.Where(d => d.State == DisbursementState.Validated)
            .Select(d => d.Id).ToHashSet();
        var validatedPaidByLine = splits.Where(s => validatedDisbursementIds.Contains(s.DisbursementId))
            .GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var graphInvoiceByLine = graphInvoiceAllocations.GroupBy(a => a.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var driftInputs = validatedPaidByLine
            .Select(kv => new LineInvoiceDriftInput(
                kv.Key,
                committedLines.FirstOrDefault(l => l.Id == kv.Key)?.LineCode ?? $"L-{kv.Key}",
                kv.Value,
                graphInvoiceByLine.TryGetValue(kv.Key, out var alloc) ? alloc : 0m))
            .ToList();
        foreach (var w in ReconciliationWarnings.EvaluateGraphInvoiceAllocationDrift(driftInputs))
        {
            Emit(new Computed(w.ScopeType, w.ScopeEntityId, w.Comparison, DiscrepancySeverity.Warning,
                w.Expected, w.Actual, w.SourceDocument));
        }

        return set;
    }
}
