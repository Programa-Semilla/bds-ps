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

/// <summary>
/// Aggregated state for one item under comparison. Applicant-level PII (legal
/// id / email / phone) lives here once per item rather than per-supplier — the
/// orchestrator threads it through to the PII redactor on every supplier block
/// it builds (FR-B2 / FINDING-6).
/// </summary>
public sealed record ItemAssembly(
    int ApplicationItemId,
    int ApplicationId,
    string ItemHeader,
    bool ApplicationIsClosed,
    string? ApplicantLegalId,
    string? ApplicantEmail,
    string? ApplicantPhone,
    IReadOnlyList<SupplierAssembly> Suppliers);

/// <summary>
/// Per-supplier assembly: structured data + blob references. SupplierLegalId
/// + Branch contact fields are the live-domain proxies for the FR-B2 owner-DNI
/// / owner-personal-phone fields (the domain has no distinct "personal" /
/// "business" split today — spec drift is documented in the spec.md follow-up).
/// </summary>
public sealed record SupplierAssembly(
    int SupplierId,
    string SupplierName,
    string SupplierLegalId,
    string SupplierVerificationStatus,
    int? SupplierBranchId,
    string? BranchName,
    string? BranchContactEmail,
    string? BranchContactPhone,
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
