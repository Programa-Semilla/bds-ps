using FundingPlatform.Application.Tranches;

namespace FundingPlatform.Web.ViewModels.Tranches;

/// <summary>
/// Spec 046 / US1 — backs the <c>_TrancheEditor</c> partial on the reviewer review surface: the
/// application's tranches (with derived amounts + members) and its budget-lines (with budget +
/// current membership). The reviewer creates/renames/deletes tranches and assigns lines here;
/// the editor is rendered only pre-audit (frozen at execution).
/// </summary>
public sealed class TrancheEditorViewModel
{
    public int ApplicationId { get; init; }

    /// <summary>Tranches with derived amounts + member line ids (ordered by ordinal).</summary>
    public IReadOnlyList<TrancheView> Tranches { get; init; } = [];

    /// <summary>All budget-lines with budget + current tranche membership.</summary>
    public IReadOnlyList<TrancheEditorLine> Lines { get; init; } = [];

    /// <summary>Σ all line budgets = the allocation; Σ tranches (incl. synthetic) equals this by construction.</summary>
    public decimal AllocationTotal => Lines.Sum(l => l.Budget);

    /// <summary>Lines with no explicit tranche → the synthetic "General" bucket.</summary>
    public IReadOnlyList<TrancheEditorLine> UnassignedLines => Lines.Where(l => l.TrancheId is null).ToList();

    /// <summary>Σ unassigned line budgets (the synthetic tranche's derived amount).</summary>
    public decimal SyntheticAmount => UnassignedLines.Sum(l => l.Budget);
}
