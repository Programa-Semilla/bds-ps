// Spec 047 — see specs/047-evidence-graph-required-docs/contracts/interfaces.md and research D3/D6.

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.DocRules;
using FundingPlatform.Application.Evidence;
using FundingPlatform.Application.Reconciliation;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Services;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 047 / US3 — implements <see cref="IBudgetLineClosureService"/>. The close gate re-reads FRESH
/// sums (the P1 R5 race lesson): completeness (both sources), all attributed payments validated, the
/// paid↔accepted equality leg, and required-evidence full allocation. Off-ledger — <see cref="Item.Close"/>
/// only flips the stored state; no ledger/balance write. Mirrors the two-SaveChanges audit discipline.
/// </summary>
public sealed class BudgetLineClosureService : IBudgetLineClosureService
{
    private readonly AppDbContext _db;
    private readonly ILineCompletenessProjection _completeness;
    private readonly IAdminAuditEventWriter _audit;
    private readonly IReconciliationMaterializer _materializer;

    public BudgetLineClosureService(
        AppDbContext db,
        ILineCompletenessProjection completeness,
        IAdminAuditEventWriter audit,
        IReconciliationMaterializer materializer)
    {
        _db = db;
        _completeness = completeness;
        _audit = audit;
        _materializer = materializer;
    }

    public async Task<LineCompleteness?> GetCompletenessAsync(int applicationId, int itemId, CancellationToken ct)
    {
        var map = await _completeness.GetForApplicationAsync(applicationId, ct);
        return map.TryGetValue(itemId, out var c) ? c : null;
    }

    public async Task<Result> CloseAsync(int applicationId, int itemId, string? reason, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var app = await _db.Applications.Include(a => a.Items)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        var item = app?.Items.FirstOrDefault(i => i.Id == itemId);
        if (app is null || item is null)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.LineNotFound, null, EvidenceReasons.LineNotFound));
        }
        if (item.ClosureState == ItemClosureState.Closed)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.AlreadyClosed, null, EvidenceReasons.AlreadyClosed));
        }

        // (1) Completeness — every required type present (both sources, D1).
        var completeness = await GetCompletenessAsync(applicationId, itemId, ct);
        if (completeness is { EvidenceIncomplete: true })
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.MissingRequiredDocuments, null, EvidenceReasons.MissingRequiredDocuments));
        }

        // (2) Every payment attributed to the line is Validated (no non-cancelled, non-validated payment).
        var hasUnvalidatedPayment = await _db.DisbursementLineAllocations.AsNoTracking()
            .AnyAsync(a => a.ItemId == itemId && _db.Disbursements.Any(d =>
                d.Id == a.DisbursementId && d.ApplicationId == applicationId
                && d.State != DisbursementState.Cancelled && d.State != DisbursementState.Validated), ct);
        if (hasUnvalidatedPayment)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.PaymentNotValidated, null, EvidenceReasons.PaymentNotValidated));
        }

        // (3) LinePaid == LineAccepted to the colón (fresh sums).
        var linePaid = await _db.DisbursementLineAllocations.AsNoTracking()
            .Where(a => a.ItemId == itemId && _db.Disbursements.Any(d =>
                d.Id == a.DisbursementId && d.State == DisbursementState.Validated))
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        // Spec edge case (spec.md line 95) — a line with no attributed (validated) payment cannot
        // satisfy the equality chain and is completed via cancellation, not closure. The zero==zero
        // trivial pass would otherwise let a no-activity line close (deep-review C3).
        if (linePaid < 0.01m)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.NoPaymentToClose, null, EvidenceReasons.NoPaymentToClose));
        }

        var lineAccepted = await _db.EvidenceLineAllocations.AsNoTracking()
            .Where(a => a.ItemId == itemId && _db.Evidence.Any(e =>
                e.Id == a.EvidenceId && e.ApplicationId == applicationId && e.Type == EvidenceType.SignedAcceptance))
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        var label = Item.FormatLabel(item.LineCode, item.ProductName, item.Id);
        var mismatch = DisbursementLineReconciliation.EvaluateLineEquality(
            new[] { new LineEqualityInput(itemId, label, linePaid, lineAccepted) });
        if (mismatch.Count > 0)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.LineEqualityMismatch, null, EvidenceReasons.LineEqualityMismatch));
        }

        // (4) Each required graph evidence linked to the line is fully allocated (Σ its allocations = amount).
        if (completeness is not null && !await RequiredEvidenceFullyAllocatedAsync(applicationId, itemId, completeness.Required, ct))
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.RequiredEvidenceNotFullyAllocated, null, EvidenceReasons.RequiredEvidenceNotFullyAllocated));
        }

        app.CloseLine(itemId, actorUserId, reason);

        await _audit.WriteAsync(
            AdminAuditEvent.ClosureLineClosed, actorUserId,
            JsonSerializer.Serialize(new { itemId, applicationId, reason }),
            ct);

        var closeResult = await CommitAsync(ct);
        if (closeResult.Succeeded)
        {
            await _materializer.MaterializeAsync(applicationId, actorUserId, ct);
        }
        return closeResult;
    }

    public async Task<Result> ReopenAsync(int applicationId, int itemId, string reason, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.ReopenReasonRequired, nameof(reason), EvidenceReasons.ReopenReasonRequired));
        }

        var app = await _db.Applications.Include(a => a.Items)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        var item = app?.Items.FirstOrDefault(i => i.Id == itemId);
        if (app is null || item is null)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.LineNotFound, null, EvidenceReasons.LineNotFound));
        }
        if (item.ClosureState == ItemClosureState.Open)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.NotClosed, null, EvidenceReasons.NotClosed));
        }

        app.ReopenLine(itemId, actorUserId, reason); // off-ledger — no balance write

        await _audit.WriteAsync(
            AdminAuditEvent.ClosureLineReopened, actorUserId,
            JsonSerializer.Serialize(new { itemId, applicationId, reason }),
            ct);

        var reopenResult = await CommitAsync(ct);
        if (reopenResult.Succeeded)
        {
            await _materializer.MaterializeAsync(applicationId, actorUserId, ct);
        }
        return reopenResult;
    }

    /// <summary>For each required type, any graph evidence of that type linked to the line must be fully
    /// allocated (Σ of the evidence's allocations across all lines equals its amount). Presence that came
    /// only from a disbursement (no graph evidence of the type) needs no allocation check — the P1/P2
    /// split integrity already holds.</summary>
    private async Task<bool> RequiredEvidenceFullyAllocatedAsync(
        int applicationId, int itemId, IReadOnlyCollection<EvidenceType> requiredTypes, CancellationToken ct)
    {
        if (requiredTypes.Count == 0)
        {
            return true;
        }

        var evidenceForLine = await _db.EvidenceLineAllocations.AsNoTracking()
            .Where(a => a.ItemId == itemId)
            .Join(_db.Evidence.AsNoTracking().Where(e => e.ApplicationId == applicationId),
                a => a.EvidenceId, e => e.Id, (a, e) => new { e.Id, e.Type, e.Amount })
            .Where(x => requiredTypes.Contains(x.Type))
            .Distinct()
            .ToListAsync(ct);
        if (evidenceForLine.Count == 0)
        {
            return true;
        }

        // Deep-review P3 — one grouped read of the per-evidence allocation totals (was an N+1 SumAsync
        // per evidence in a loop). Each required graph evidence must be fully allocated (Σ = amount).
        var evidenceIds = evidenceForLine.Select(e => e.Id).ToList();
        var allocatedById = await _db.EvidenceLineAllocations.AsNoTracking()
            .Where(a => evidenceIds.Contains(a.EvidenceId))
            .GroupBy(a => a.EvidenceId)
            .Select(g => new { EvidenceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.EvidenceId, x => x.Total, ct);

        foreach (var e in evidenceForLine)
        {
            var totalAllocated = allocatedById.GetValueOrDefault(e.Id, 0m);
            if (Math.Abs(totalAllocated - e.Amount) >= 0.01m)
            {
                return false;
            }
        }
        return true;
    }

    private async Task<Result> CommitAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new DomainError(EvidenceReasons.Codes.Concurrency, null, EvidenceReasons.Concurrency));
        }
    }
}
