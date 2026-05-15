using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Application.Abstractions.AiComparison;

/// <summary>
/// Spec 020 / FR-C1 — single Application → AI pipeline entry point. Encapsulates
/// extract → normalize → compare plus guard checks, cache lookups, and audit
/// emission. Web depends on this interface only.
/// </summary>
public interface IComparisonOrchestrator
{
    Task<GenerateComparisonResult> GenerateAsync(
        GenerateComparisonCommand command, CancellationToken cancellationToken);

    Task<ItemStatusResult> GetStatusAsync(
        int applicationItemId, CancellationToken cancellationToken);

    /// <summary>Read-only cache fetch used by the review page render path. No AI calls.</summary>
    Task<CachedComparisonResult?> GetCachedComparisonAsync(
        int applicationItemId, CancellationToken cancellationToken);
}

public sealed record GenerateComparisonCommand(
    int ApplicationItemId,
    string ActorUserId,
    string ActorRole,
    bool BypassRateLimit,
    bool BypassTokenCap,
    bool ForceRegenerate = false);

public abstract record GenerateComparisonResult;

public sealed record GenerateComparisonSuccess(
    int ApplicationItemId,
    string ArtifactJson,
    DateTimeOffset GeneratedAt,
    Freshness Freshness,
    IReadOnlyList<ChangedInput> ChangedInputs) : GenerateComparisonResult;

public sealed record GenerateComparisonFailure(
    int ApplicationItemId,
    string FailureReason,
    string? ProviderCode = null,
    string? OffendingInput = null,
    int? EstimatedTokens = null,
    int? Cap = null,
    DateTimeOffset? WindowResetsAt = null) : GenerateComparisonResult;

public sealed record CachedComparisonResult(
    int ApplicationItemId,
    string ArtifactJson,
    DateTimeOffset GeneratedAt,
    Freshness Freshness,
    IReadOnlyList<ChangedInput> ChangedInputs);

public sealed record ItemStatusResult(
    int ApplicationItemId,
    ItemState State,
    Freshness Freshness,
    IReadOnlyList<ChangedInput> ChangedInputs,
    DateTimeOffset? LastUpdatedAt,
    string? FailureReason);

public enum ItemState
{
    None,
    CachedFresh,
    CachedStale,
    Pending,
    Running,
    Failed,
}

public enum Freshness
{
    None,
    Fresh,
    Stale,
}
