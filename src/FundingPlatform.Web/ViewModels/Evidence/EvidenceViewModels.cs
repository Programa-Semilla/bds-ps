using FundingPlatform.Application.Evidence;

namespace FundingPlatform.Web.ViewModels.Evidence;

/// <summary>Spec 047 — a budget-line option for the allocation editor (line id + display label).</summary>
public sealed record EvidenceLineOption(int ItemId, string Label);

/// <summary>Spec 047 — inputs for the per-line allocation editor partial. <see cref="Current"/> is
/// the existing per-line amount map (empty for the attach form).</summary>
public sealed class AllocationEditorViewModel
{
    public required IReadOnlyList<EvidenceLineOption> Lines { get; init; }
    public IReadOnlyDictionary<int, decimal>? Current { get; init; }
}

/// <summary>Spec 047 — the evidence list surface for one application.</summary>
public sealed class EvidenceIndexViewModel
{
    public required int ApplicationId { get; init; }
    public required IReadOnlyList<EvidenceSummary> Items { get; init; }
    public required bool CanWrite { get; init; }
    public required string AcceptExtensions { get; init; }
    public required IReadOnlyList<EvidenceLineOption> Lines { get; init; }
}

/// <summary>Spec 047 — one evidence list row.</summary>
public sealed class EvidenceRowViewModel
{
    public required int ApplicationId { get; init; }
    public required EvidenceSummary Item { get; init; }
    public required bool CanWrite { get; init; }
}

/// <summary>Spec 047 — the evidence detail surface (allocations + version chain).</summary>
public sealed class EvidenceDetailViewModel
{
    public required int ApplicationId { get; init; }
    public required EvidenceDetail Detail { get; init; }
    public required bool CanWrite { get; init; }
    public required string AcceptExtensions { get; init; }
    public required IReadOnlyList<EvidenceLineOption> Lines { get; init; }
}
