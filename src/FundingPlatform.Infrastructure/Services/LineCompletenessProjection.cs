// Spec 047 — see specs/047-evidence-graph-required-docs/data-model.md (completeness reads both sources, D1).

using FundingPlatform.Application.DocRules;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 047 — implements <see cref="ILineCompletenessProjection"/>. Per line, the present evidence
/// types are the union of (a) graph <see cref="Evidence"/> types linked to the line and (b) the
/// bank-receipt/invoice kinds carried by VALIDATED disbursements that paid the line (D1). Required
/// types come from the <see cref="IDocumentRuleService"/> resolver (category set → global default).
/// Batched: three application-scoped reads, no per-line round-trip.
/// </summary>
public sealed class LineCompletenessProjection : ILineCompletenessProjection
{
    private readonly AppDbContext _db;
    private readonly IDocumentRuleService _docRules;

    public LineCompletenessProjection(AppDbContext db, IDocumentRuleService docRules)
    {
        _db = db;
        _docRules = docRules;
    }

    public async Task<IReadOnlyDictionary<int, LineCompleteness>> GetForApplicationAsync(int applicationId, CancellationToken ct)
    {
        var resolver = await _docRules.BuildResolverAsync(ct);

        var items = await _db.Items.AsNoTracking()
            .Where(i => i.ApplicationId == applicationId)
            .Select(i => new { i.Id, i.CategoryId })
            .ToListAsync(ct);

        // (a) graph evidence types linked to each line.
        var graphRows = await _db.EvidenceLineAllocations.AsNoTracking()
            .Where(a => _db.Evidence.Any(e => e.Id == a.EvidenceId && e.ApplicationId == applicationId))
            .Join(_db.Evidence.AsNoTracking(), a => a.EvidenceId, e => e.Id, (a, e) => new { a.ItemId, e.Type })
            .ToListAsync(ct);
        var graphByItem = graphRows.GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Type).ToHashSet());

        // (b) bank-receipt/invoice kinds from VALIDATED disbursements that paid each line.
        var disbRows = await _db.DisbursementLineAllocations.AsNoTracking()
            .Where(da => _db.Disbursements.Any(d => d.Id == da.DisbursementId
                && d.ApplicationId == applicationId && d.State == DisbursementState.Validated))
            .Join(_db.DisbursementEvidence.AsNoTracking(), da => da.DisbursementId, de => de.DisbursementId,
                (da, de) => new { da.ItemId, de.Kind })
            .ToListAsync(ct);
        var disbByItem = disbRows.GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Select(x => MapKind(x.Kind)).ToHashSet());

        var result = new Dictionary<int, LineCompleteness>(items.Count);
        foreach (var item in items)
        {
            var present = new HashSet<EvidenceType>();
            if (graphByItem.TryGetValue(item.Id, out var g))
            {
                present.UnionWith(g);
            }
            if (disbByItem.TryGetValue(item.Id, out var d))
            {
                present.UnionWith(d);
            }

            var required = resolver.RequiredFor(item.CategoryId);
            var missing = Item.MissingRequiredDocuments(required, present).ToList();

            result[item.Id] = new LineCompleteness(item.Id, required, present.ToList(), missing);
        }

        return result;
    }

    private static EvidenceType MapKind(EvidenceKind kind) => kind switch
    {
        EvidenceKind.BankReceipt => EvidenceType.BankReceipt,
        EvidenceKind.Invoice => EvidenceType.Invoice,
        _ => EvidenceType.Other,
    };
}
