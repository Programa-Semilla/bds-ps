using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 040 / D4–D5 — read access to checklist templates for the workflow gates.
/// Admin CRUD lives in <c>IChecklistTemplateService</c> (folds DB access in, mirrors
/// <c>FundService</c>); this repository serves the deterministic "active template for
/// stage X" resolution the reviewer/auditor gates depend on.
/// </summary>
public interface IChecklistTemplateRepository
{
    /// <summary>
    /// The active template that applies to <paramref name="stage"/>, with its items
    /// loaded. "Applies" = <c>AppliesToStage</c> is <paramref name="stage"/> or
    /// <see cref="ChecklistStage.Both"/>; a stage-specific active template wins over a
    /// <c>Both</c> one. Returns <c>null</c> when no active template applies.
    /// </summary>
    Task<ChecklistTemplate?> GetActiveForStageAsync(ChecklistStage stage, CancellationToken ct);
}
