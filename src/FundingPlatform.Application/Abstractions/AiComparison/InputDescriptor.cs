namespace FundingPlatform.Application.Abstractions.AiComparison;

/// <summary>
/// Spec 020 / FR-D2 — canonical input to <c>InputHasher.Compute</c>. Pure value
/// carrier; the hasher freezes it into a SHA-256 hex string with deterministic
/// JSON canonicalization (sorted keys, declared array order, no whitespace).
/// </summary>
public sealed record InputDescriptor(
    int ApplicationItemId,
    IReadOnlyList<int> OrderedSupplierIds,
    IReadOnlyList<int> OrderedBranchIds,
    IReadOnlyList<BlobReference> BlobReferences,
    IReadOnlyList<LineState> LineState,
    string PromptVersion,
    string SchemaVersion);

public sealed record BlobReference(Guid BlobId, string ContentHash);

public sealed record LineState(
    int QuotationLineId,
    decimal Quantity,
    decimal UnitPrice,
    string CurrencyCode,
    Guid? ExchangeRateSnapshotId);
