using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 047 / FR-028 (research D5) — one row of a <see cref="DocumentRuleSet"/>: whether a given
/// <see cref="EvidenceType"/> is required. Owned by the set; edits are full-replace (no snapshot
/// table — completeness is computed live, closure is a stored terminal, so nothing references these
/// rows historically).
/// </summary>
public sealed class DocumentRuleItem
{
    public int Id { get; private set; }
    public int DocumentRuleSetId { get; private set; }
    public EvidenceType EvidenceType { get; private set; }
    public bool IsRequired { get; private set; }

    private DocumentRuleItem() { } // EF

    internal DocumentRuleItem(EvidenceType evidenceType, bool isRequired)
    {
        EvidenceType = evidenceType;
        IsRequired = isRequired;
    }
}
