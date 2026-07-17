using FundingPlatform.Application.Admin.Users.DTOs;

namespace FundingPlatform.Application.DocRules;

/// <summary>
/// Spec 047 / FR-028 — admin CRUD for the required-document rule matrix (one set per category + a
/// global default) and the resolver the completeness projection uses. Admin-only; the controller
/// owns authorization. Mirrors <c>ChecklistTemplateService</c> but simpler — full-replace items, no
/// response-snapshot table (D5).
/// </summary>
public interface IDocumentRuleService
{
    /// <summary>All rule sets (global default first, then per-category), each with its required types.</summary>
    Task<IReadOnlyList<DocumentRuleSetRow>> ListAsync(CancellationToken ct);

    /// <summary>The rule set for a category (null = global default) as a full six-type selection grid,
    /// or null when the category id does not exist. A category with no set yet yields an all-false grid.</summary>
    Task<DocumentRuleSetDetail?> GetAsync(int? categoryId, CancellationToken ct);

    /// <summary>FR-028 — create-or-replace a category's (or the global default's) required-type set.
    /// Audited <c>docrule.upserted</c>.</summary>
    Task<Result> UpsertAsync(UpsertDocumentRuleCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>Builds an in-memory resolver over all rule sets for the completeness projection.</summary>
    Task<IDocumentRuleResolver> BuildResolverAsync(CancellationToken ct);
}
