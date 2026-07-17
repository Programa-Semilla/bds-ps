using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 047 / FR-028 (research D5) — an admin-configured required-document rule set, keyed by
/// <see cref="CategoryId"/> (null = the global default). Owns <see cref="DocumentRuleItem"/> rows
/// (one per <see cref="EvidenceType"/> that is required). Mirrors <c>ChecklistTemplate</c>'s admin
/// CRUD but WITHOUT a response-snapshot table — completeness is computed live and closure is a
/// stored terminal, so a full-replace of items is safe (nothing references item rows historically).
/// One set per category (+ one global default), enforced by <c>UNIQUE (CategoryId)</c> and the
/// service.
/// </summary>
public sealed class DocumentRuleSet
{
    private readonly List<DocumentRuleItem> _items = [];

    public int Id { get; private set; }
    /// <summary>Null = the global-default rule set applied when a category has no set of its own.</summary>
    public int? CategoryId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<DocumentRuleItem> Items => _items.AsReadOnly();

    private DocumentRuleSet() { } // EF

    public static DocumentRuleSet Create(int? categoryId)
        => new() { CategoryId = categoryId };

    /// <summary>Full-replace the required-type rows. Only rows flagged required need be supplied;
    /// duplicates by type are collapsed (last wins).</summary>
    public void ReplaceItems(IEnumerable<(EvidenceType Type, bool IsRequired)> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        foreach (var group in items.GroupBy(i => i.Type))
        {
            var isRequired = group.Last().IsRequired;
            _items.Add(new DocumentRuleItem(group.Key, isRequired));
        }
    }

    /// <summary>The evidence types this set marks as required.</summary>
    public IReadOnlyCollection<EvidenceType> RequiredTypes()
        => _items.Where(i => i.IsRequired).Select(i => i.EvidenceType).ToList();
}
