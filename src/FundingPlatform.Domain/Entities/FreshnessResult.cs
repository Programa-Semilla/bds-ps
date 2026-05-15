namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 020 / FR-D3 — result of comparing a cached <see cref="ComparisonArtifact"/>
/// against the live <c>InputDescriptor</c>. When <see cref="IsFresh"/> is false,
/// <see cref="ChangedInputs"/> enumerates which dimension(s) of the input drifted
/// so the UI can render the human-readable "Datos desactualizados" badge.
/// </summary>
public sealed record FreshnessResult(bool IsFresh, IReadOnlyList<ChangedInput> ChangedInputs)
{
    public static readonly FreshnessResult Fresh =
        new(true, Array.Empty<ChangedInput>());

    public static FreshnessResult Stale(params ChangedInput[] inputs) =>
        new(false, inputs);
}

/// <summary>
/// The dimensions that can drift between a cached artifact and the live state.
/// Mirrored 1:1 from contracts/ai-client.md so the orchestrator and the
/// front-end share a stable vocabulary.
/// </summary>
public enum ChangedInput
{
    FileAdded,
    FileRemoved,
    LineEdited,
    SupplierAdded,
    SupplierRemoved,
    SnapshotChanged,
    SchemaBumped,
    PromptVersionBumped,
}
