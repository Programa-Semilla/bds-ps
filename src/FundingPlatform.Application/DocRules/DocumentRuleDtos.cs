using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.DocRules;

/// <summary>Spec 047 — one required-type selection for the admin matrix (per evidence type).</summary>
public sealed record DocumentRuleTypeSelection(EvidenceType Type, bool IsRequired);

/// <summary>Spec 047 / FR-028 — full-replace the required-document rule set for a category
/// (<c>CategoryId</c> null = the global default). Only types flagged required matter; the rest are
/// treated as not-required.</summary>
public sealed record UpsertDocumentRuleCommand(int? CategoryId, IReadOnlyList<DocumentRuleTypeSelection> Items);

/// <summary>Spec 047 — a rule-set list row: the category (null = global default) + its required types.</summary>
public sealed record DocumentRuleSetRow(
    int? CategoryId,
    string CategoryName,
    IReadOnlyCollection<EvidenceType> RequiredTypes);

/// <summary>Spec 047 — a rule-set for the edit form: every evidence type paired with its required flag.</summary>
public sealed record DocumentRuleSetDetail(
    int? CategoryId,
    string CategoryName,
    IReadOnlyList<DocumentRuleTypeSelection> Selections);

/// <summary>Spec 047 — a resolved snapshot of all rule sets, used by the completeness projection to
/// resolve a line's required types (its category's set, else the global default, else empty) without
/// a per-line DB round-trip.</summary>
public interface IDocumentRuleResolver
{
    IReadOnlyCollection<EvidenceType> RequiredFor(int? categoryId);
}
