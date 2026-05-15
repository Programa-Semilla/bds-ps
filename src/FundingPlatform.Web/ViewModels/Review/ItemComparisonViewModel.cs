using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Web.ViewModels.Review;

/// <summary>
/// Spec 020 — per-item comparison region projection. Carries the artifact
/// JSON (rendered by _ComparisonRegion.cshtml), the freshness signal, and the
/// changed-input list for the "Datos desactualizados" badge.
/// </summary>
public class ItemComparisonViewModel
{
    public int ApplicationItemId { get; set; }
    public bool HasArtifact { get; set; }
    public string? ArtifactJson { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public Freshness Freshness { get; set; } = Freshness.None;
    public IReadOnlyList<ChangedInput> ChangedInputs { get; set; } = Array.Empty<ChangedInput>();
    public bool HasMinimumSuppliers { get; set; }
    public bool IsAdmin { get; set; }
    public string? FailureReason { get; set; }
    public string? PendingState { get; set; } // "Pending" | "Running" | null
}
