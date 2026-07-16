using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Application.Disbursements;

/// <summary>Spec 045 / FR-001 — record a disbursement against an executed agreement.</summary>
public sealed record RecordDisbursementCommand(
    int ApplicationId,
    DateOnly PaymentDate,
    decimal Amount,
    string BankTransactionReference,
    string? BankAccountReference);

/// <summary>Spec 045 / FR-028 — edit a pre-validation disbursement's details.</summary>
public sealed record EditDisbursementCommand(
    int ApplicationId,
    int DisbursementId,
    DateOnly PaymentDate,
    decimal Amount,
    string BankTransactionReference,
    string? BankAccountReference);

/// <summary>Spec 045 / FR-006/FR-010 — attach or replace one typed evidence document.
/// The content stream is already type-validated + size-bounded at the controller boundary.</summary>
public sealed record AttachDisbursementEvidenceCommand(
    int ApplicationId,
    int DisbursementId,
    EvidenceKind Kind,
    decimal Amount,
    string Currency,
    string DocumentReferenceNumber,
    DateOnly DocumentDate,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSize);

/// <summary>Spec 045 — flat read projection for the disbursement list.</summary>
public sealed record DisbursementListItem(
    int Id,
    DateOnly PaymentDate,
    decimal Amount,
    DisbursementState State,
    bool HasBankReceipt,
    bool HasInvoice,
    bool IsValidatable);

/// <summary>Spec 045 — a stored evidence document summary shown on the detail surface.</summary>
public sealed record DisbursementEvidenceSummary(
    EvidenceKind Kind,
    decimal Amount,
    string Currency,
    string DocumentReferenceNumber,
    DateOnly DocumentDate,
    string OriginalFileName,
    string UploadedByDisplayName,
    DateTimeOffset UploadedAtUtc);

/// <summary>Spec 045 — the full disbursement detail: amounts, evidence, and the live
/// (recomputed-on-read) discrepancy list (research R4).</summary>
public sealed record DisbursementDetail(
    int Id,
    int ApplicationId,
    DateOnly PaymentDate,
    decimal Amount,
    string BankTransactionReference,
    string? BankAccountReference,
    DisbursementState State,
    string CreatedByDisplayName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ValidatedAtUtc,
    IReadOnlyList<DisbursementEvidenceSummary> Evidence,
    IReadOnlyList<ReconciliationDiscrepancy> Discrepancies,
    bool IsValidatable);

/// <summary>Spec 045 — a resolved BackendStream serving handle for an evidence download.</summary>
public sealed record DisbursementEvidenceDownload(
    Stream Content,
    string ContentType,
    string FileName);
