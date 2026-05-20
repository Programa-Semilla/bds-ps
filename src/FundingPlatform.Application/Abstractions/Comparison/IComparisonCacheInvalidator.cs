namespace FundingPlatform.Application.Abstractions.Comparison;

/// <summary>
/// Spec 023 / FR-009 — narrow seam for invalidating the spec-020 AI quote
/// comparison cache when the applicant edits a quotation. The Application
/// service calls this AFTER the edit commits; the next reviewer
/// <c>Generar todo</c> run picks up the cache miss and regenerates.
///
/// The interface is intentionally minimal so Application code stays decoupled
/// from spec 020's <c>ComparisonArtifact</c> read models. The Infrastructure
/// implementation deletes the artifact row(s) keyed on the Item; the reviewer
/// freshness path then re-renders the empty-state without intervention.
/// </summary>
public interface IComparisonCacheInvalidator
{
    /// <summary>
    /// Synchronously invalidates the cached comparison for a single Item. Idempotent.
    /// </summary>
    Task InvalidateForItemAsync(int itemId, CancellationToken ct = default);
}
