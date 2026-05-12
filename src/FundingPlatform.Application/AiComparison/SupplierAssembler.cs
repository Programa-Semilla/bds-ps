using FundingPlatform.Application.Abstractions.AiComparison;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-B1 — per-item supplier-data assembly contract. Implementations
/// live in Infrastructure (EF query) so this layer stays free of EF.
/// </summary>
public interface ISupplierAssembler
{
    Task<ItemAssembly?> AssembleAsync(int applicationItemId, CancellationToken ct);
}

/// <summary>Aggregated state for one item under comparison.</summary>
public sealed record ItemAssembly(
    int ApplicationItemId,
    int ApplicationId,
    string ItemHeader,
    bool ApplicationIsClosed,
    IReadOnlyList<SupplierAssembly> Suppliers);

/// <summary>Per-supplier assembly: structured data + blob references.</summary>
public sealed record SupplierAssembly(
    int SupplierId,
    string SupplierName,
    string SupplierLegalId,
    string SupplierVerificationStatus,
    int? SupplierBranchId,
    string? BranchName,
    decimal Price,
    string CurrencyCode,
    decimal? ConvertedCrcAmount,
    decimal? SnapshotRateValue,
    Guid? SnapshotRateId,
    DateOnly ValidUntil,
    int DocumentId,
    string DocumentFileName,
    string DocumentBlobKey,
    long DocumentFileSize,
    IReadOnlyList<BlobReference> Blobs);
