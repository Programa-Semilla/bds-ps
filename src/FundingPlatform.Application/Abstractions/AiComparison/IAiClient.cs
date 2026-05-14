namespace FundingPlatform.Application.Abstractions.AiComparison;

/// <summary>
/// Spec 020 / NFR-M1 — provider-agnostic AI seam. The Anthropic implementation
/// lives in Infrastructure; adding a second provider in a future spec must
/// touch only the provider folder + DI registration.
/// </summary>
public interface IAiClient
{
    Task<ExtractResult> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken);
    Task<CompareResult> CompareAsync(CompareRequest request, CancellationToken cancellationToken);
}

public sealed record ExtractRequest(
    string Model,
    string PromptText,
    string SchemaJson,
    IReadOnlyList<AiInputBlock> Blocks);

public sealed record CompareRequest(
    string Model,
    string PromptText,
    string SchemaJson,
    string NormalizedSuppliersJson);

public abstract record AiInputBlock;

public sealed record TextBlock(string Text) : AiInputBlock;

public sealed record PdfBlock(Guid BlobId, ReadOnlyMemory<byte> Bytes) : AiInputBlock;

public sealed record ExtractResult(
    string Json,
    int TokenCostInput,
    int TokenCostOutput,
    int LatencyMs);

public sealed record CompareResult(
    string Json,
    int TokenCostInput,
    int TokenCostOutput,
    int LatencyMs);
