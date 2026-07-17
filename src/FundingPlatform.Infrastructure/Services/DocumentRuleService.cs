// Spec 047 — see specs/047-evidence-graph-required-docs/contracts/interfaces.md and research D5.

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Admin.Users.DTOs;
using FundingPlatform.Application.DocRules;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 047 — implements <see cref="IDocumentRuleService"/>. Mirrors <c>ChecklistTemplateService</c>'s
/// admin CRUD + two-SaveChanges audit, but simpler: full-replace items, no response-snapshot table
/// (D5). One set per category enforced by a pre-check + the <c>UX_DocumentRuleSets_CategoryId</c>
/// backstop.
/// </summary>
public sealed class DocumentRuleService : IDocumentRuleService
{
    private const string GlobalDefaultLabel = "__global_default__";

    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;

    public DocumentRuleService(AppDbContext db, IAdminAuditEventWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<DocumentRuleSetRow>> ListAsync(CancellationToken ct)
    {
        var sets = await _db.DocumentRuleSets.AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.CategoryId,
                RequiredTypes = _db.DocumentRuleItems
                    .Where(i => i.DocumentRuleSetId == s.Id && i.IsRequired)
                    .Select(i => i.EvidenceType).ToList(),
            })
            .ToListAsync(ct);

        var categoryIds = sets.Where(s => s.CategoryId != null).Select(s => s.CategoryId!.Value).ToList();
        var names = await _db.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Global default first, then per-category by name.
        return sets
            .Select(s => new DocumentRuleSetRow(
                s.CategoryId,
                s.CategoryId is null ? GlobalDefaultLabel : names.GetValueOrDefault(s.CategoryId.Value, $"#{s.CategoryId}"),
                s.RequiredTypes))
            .OrderBy(r => r.CategoryId is null ? 0 : 1)
            .ThenBy(r => r.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<DocumentRuleSetDetail?> GetAsync(int? categoryId, CancellationToken ct)
    {
        string categoryName;
        if (categoryId is { } cid)
        {
            var name = await _db.Categories.AsNoTracking()
                .Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct);
            if (name is null)
            {
                return null;
            }
            categoryName = name;
        }
        else
        {
            categoryName = GlobalDefaultLabel;
        }

        var required = await _db.DocumentRuleSets.AsNoTracking()
            .Where(s => s.CategoryId == categoryId)
            .SelectMany(s => _db.DocumentRuleItems.Where(i => i.DocumentRuleSetId == s.Id && i.IsRequired))
            .Select(i => i.EvidenceType)
            .ToListAsync(ct);
        var requiredSet = required.ToHashSet();

        var selections = Enum.GetValues<EvidenceType>()
            .Select(t => new DocumentRuleTypeSelection(t, requiredSet.Contains(t)))
            .ToList();

        return new DocumentRuleSetDetail(categoryId, categoryName, selections);
    }

    public async Task<Result> UpsertAsync(UpsertDocumentRuleCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        if (cmd.CategoryId is { } cid && !await _db.Categories.AnyAsync(c => c.Id == cid, ct))
        {
            return Result.Failure(new DomainError(DocRuleReasons.Codes.CategoryNotFound, null, DocRuleReasons.CategoryNotFound));
        }

        var set = await _db.DocumentRuleSets
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.CategoryId == cmd.CategoryId, ct);

        var isNew = set is null;
        if (set is null)
        {
            set = DocumentRuleSet.Create(cmd.CategoryId);
            _db.DocumentRuleSets.Add(set);
        }

        set.ReplaceItems(cmd.Items.Select(i => (i.Type, i.IsRequired)));

        await _audit.WriteAsync(
            AdminAuditEvent.DocRuleUpserted, actorUserId,
            JsonSerializer.Serialize(new { categoryId = cmd.CategoryId ?? 0, isNew }),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new DomainError(DocRuleReasons.Codes.Concurrency, null, DocRuleReasons.Concurrency));
        }
        catch (DbUpdateException)
        {
            // Concurrent first-insert of the same category's set lost the UX_DocumentRuleSets_CategoryId race.
            return Result.Failure(new DomainError(DocRuleReasons.Codes.DuplicateCategory, null, DocRuleReasons.DuplicateCategory));
        }

        return Result.Success();
    }

    public async Task<IDocumentRuleResolver> BuildResolverAsync(CancellationToken ct)
    {
        var rows = await _db.DocumentRuleSets.AsNoTracking()
            .Select(s => new
            {
                s.CategoryId,
                RequiredTypes = _db.DocumentRuleItems
                    .Where(i => i.DocumentRuleSetId == s.Id && i.IsRequired)
                    .Select(i => i.EvidenceType).ToList(),
            })
            .ToListAsync(ct);

        var perCategory = rows.Where(r => r.CategoryId != null)
            .ToDictionary(r => r.CategoryId!.Value, r => (IReadOnlyCollection<EvidenceType>)r.RequiredTypes);
        var global = rows.FirstOrDefault(r => r.CategoryId is null)?.RequiredTypes
            ?? (IReadOnlyCollection<EvidenceType>)[];

        return new DocumentRuleResolver(perCategory, global);
    }

    private sealed class DocumentRuleResolver : IDocumentRuleResolver
    {
        private readonly IReadOnlyDictionary<int, IReadOnlyCollection<EvidenceType>> _perCategory;
        private readonly IReadOnlyCollection<EvidenceType> _global;

        public DocumentRuleResolver(
            IReadOnlyDictionary<int, IReadOnlyCollection<EvidenceType>> perCategory,
            IReadOnlyCollection<EvidenceType> global)
        {
            _perCategory = perCategory;
            _global = global;
        }

        public IReadOnlyCollection<EvidenceType> RequiredFor(int? categoryId)
        {
            // A category with its own set uses it (even when empty — an explicit "nothing required");
            // a category with no set falls back to the global default.
            if (categoryId is { } cid && _perCategory.TryGetValue(cid, out var types))
            {
                return types;
            }
            return _global;
        }
    }
}
