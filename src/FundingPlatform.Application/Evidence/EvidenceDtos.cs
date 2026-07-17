using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Evidence;

/// <summary>Spec 047 — one line of an evidence document's per-line allocation: a portion attributed
/// to a budget-line (<see cref="Domain.Entities.Item"/>).</summary>
public sealed record EvidenceLineAllocationInput(int ItemId, decimal Amount);

/// <summary>Spec 047 / FR-002 — attach a new evidence document (with its initial version v1).
/// The content stream is already type-validated + size-bounded at the controller boundary.
/// <paramref name="Lines"/> may be empty when a <paramref name="DisbursementId"/> anchors the
/// document (orphan guard: ≥1 line OR a disbursement).</summary>
public sealed record AttachEvidenceCommand(
    int ApplicationId,
    EvidenceType Type,
    int? DisbursementId,
    decimal Amount,
    string Currency,
    string DocumentReferenceNumber,
    DateOnly DocumentDate,
    int? SupplierId,
    IReadOnlyList<EvidenceLineAllocationInput> Lines,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSize);

/// <summary>Spec 047 / FR-021 — append a new evidence version. A non-empty <paramref name="Reason"/>
/// is required. When <paramref name="Content"/> is null the file is unchanged (a reconciliation-
/// critical field edit still appends a version); otherwise a new file is uploaded and hashed.</summary>
public sealed record ReplaceEvidenceCommand(
    int ApplicationId,
    int EvidenceId,
    string Reason,
    decimal Amount,
    string Currency,
    string DocumentReferenceNumber,
    DateOnly DocumentDate,
    Stream? Content,
    string? FileName,
    string? ContentType,
    long? FileSize);

/// <summary>Spec 047 / FR-003 — replace-all the evidence's per-line allocation rows (Σ ≤ amount).</summary>
public sealed record AllocateEvidenceCommand(
    int ApplicationId,
    int EvidenceId,
    IReadOnlyList<EvidenceLineAllocationInput> Lines);

/// <summary>Spec 047 — one per-line allocation row for display (line label + amount).</summary>
public sealed record EvidenceLineAllocationRow(int ItemId, string LineLabel, decimal Amount);

/// <summary>Spec 047 — an evidence list row.</summary>
public sealed record EvidenceSummary(
    int Id,
    EvidenceType Type,
    decimal Amount,
    string Currency,
    string DocumentReferenceNumber,
    DateOnly DocumentDate,
    string? SupplierName,
    string OriginalFileName,
    string UploadedByDisplayName,
    DateTimeOffset UploadedAtUtc,
    int VersionCount,
    decimal AllocatedTotal,
    int AllocatedLineCount);

/// <summary>Spec 047 — the full evidence detail: current values, per-line allocations, version chain.</summary>
public sealed record EvidenceDetail(
    int Id,
    int ApplicationId,
    EvidenceType Type,
    int? DisbursementId,
    decimal Amount,
    string Currency,
    string DocumentReferenceNumber,
    DateOnly DocumentDate,
    string? SupplierName,
    string OriginalFileName,
    string UploadedByDisplayName,
    DateTimeOffset UploadedAtUtc,
    IReadOnlyList<EvidenceLineAllocationRow> Allocations,
    IReadOnlyList<EvidenceVersionRow> Versions);

/// <summary>Spec 047 / FR-021 — one row of the append-only version chain.</summary>
public sealed record EvidenceVersionRow(
    int VersionNumber,
    bool IsCurrent,
    string OriginalFileName,
    decimal Amount,
    string Currency,
    string DocumentReferenceNumber,
    DateOnly DocumentDate,
    string FileHash,
    string? Reason,
    string CreatedByDisplayName,
    DateTimeOffset CreatedAtUtc);

/// <summary>Spec 047 — a resolved BackendStream serving handle for an evidence (version) download.</summary>
public sealed record EvidenceDownload(
    Stream Content,
    string ContentType,
    string FileName);
