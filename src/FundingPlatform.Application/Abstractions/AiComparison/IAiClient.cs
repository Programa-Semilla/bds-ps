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

/// <summary>
/// Spec 020 — a supplier attachment the provider reads as an image rather than
/// a document (phone photos of a printed quotation are a normal upload). Kept
/// distinct from <see cref="PdfBlock"/> because the two map to different
/// provider content blocks; sending image bytes as <c>application/pdf</c> is
/// rejected outright.
/// </summary>
public sealed record ImageBlock(Guid BlobId, ReadOnlyMemory<byte> Bytes, string MediaType) : AiInputBlock;

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
