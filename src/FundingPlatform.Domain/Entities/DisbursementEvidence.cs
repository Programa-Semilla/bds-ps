using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 045 / FR-006 — one typed evidence document (bank receipt or invoice) attached
/// to a <see cref="Disbursement"/>. Exactly one of each kind is required before the
/// disbursement can be validated (enforced by <c>UX_DisbursementEvidence_Disbursement_Kind</c>
/// + the Validar completeness gate). The reconciled <see cref="Amount"/> is the figure
/// compared against the disbursement (research R4). Only CRC is accepted in P1 (FR-004).
/// </summary>
public sealed class DisbursementEvidence
{
    /// <summary>P1 accepts only the platform base currency.</summary>
    public const string RequiredCurrency = "CRC";

    public int Id { get; private set; }
    public int DisbursementId { get; private set; }
    public EvidenceKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = RequiredCurrency;
    public string DocumentReferenceNumber { get; private set; } = string.Empty;
    public DateOnly DocumentDate { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string BlobKey { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public string UploadedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private DisbursementEvidence() { } // EF

    /// <summary>
    /// FR-006/FR-004 — attach a new evidence document. Gate: the disbursement is
    /// pre-validation (Recorded/Inconsistent), the amount is positive, and the currency
    /// is CRC. The file metadata is already type-validated + size-bounded at the
    /// controller boundary (FR-008).
    /// </summary>
    public static DisbursementEvidence Attach(
        Disbursement disbursement,
        EvidenceKind kind,
        decimal amount,
        string currency,
        string documentReferenceNumber,
        DateOnly documentDate,
        string originalFileName,
        string blobKey,
        long fileSize,
        string contentType,
        string uploadedByUserId)
    {
        ArgumentNullException.ThrowIfNull(disbursement);
        GuardPreValidation(disbursement);
        Guard(amount, currency, documentReferenceNumber, originalFileName, blobKey, fileSize, contentType, uploadedByUserId);

        return new DisbursementEvidence
        {
            DisbursementId = disbursement.Id,
            Kind = kind,
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant(),
            DocumentReferenceNumber = documentReferenceNumber.Trim(),
            DocumentDate = documentDate,
            OriginalFileName = originalFileName.Trim(),
            BlobKey = blobKey,
            FileSize = fileSize,
            ContentType = contentType,
            UploadedByUserId = uploadedByUserId,
            UploadedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>FR-010 — overwrite the file + reconciled fields in place (no version chain in P1).
    /// Same pre-validation + positive-amount + CRC gate as <see cref="Attach"/>. The Kind is fixed
    /// (a bank receipt stays a bank receipt).</summary>
    public void Replace(
        Disbursement disbursement,
        decimal amount,
        string currency,
        string documentReferenceNumber,
        DateOnly documentDate,
        string originalFileName,
        string blobKey,
        long fileSize,
        string contentType,
        string uploadedByUserId)
    {
        ArgumentNullException.ThrowIfNull(disbursement);
        GuardPreValidation(disbursement);
        Guard(amount, currency, documentReferenceNumber, originalFileName, blobKey, fileSize, contentType, uploadedByUserId);

        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        DocumentReferenceNumber = documentReferenceNumber.Trim();
        DocumentDate = documentDate;
        OriginalFileName = originalFileName.Trim();
        BlobKey = blobKey;
        FileSize = fileSize;
        ContentType = contentType;
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void GuardPreValidation(Disbursement disbursement)
    {
        if (!disbursement.IsPreValidation)
        {
            throw new InvalidOperationException(
                $"Evidence can only be attached or replaced while the disbursement is pre-validation; "
                + $"disbursement {disbursement.Id} is {disbursement.State} (FR-010).");
        }
    }

    private static void Guard(
        decimal amount,
        string currency,
        string documentReferenceNumber,
        string originalFileName,
        string blobKey,
        long fileSize,
        string contentType,
        string uploadedByUserId)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Evidence amount must be greater than zero.");
        }
        if (!string.Equals(currency?.Trim(), RequiredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Only {RequiredCurrency} is accepted in this slice (FR-004); got '{currency}'.");
        }
        if (string.IsNullOrWhiteSpace(documentReferenceNumber))
        {
            throw new ArgumentException("DocumentReferenceNumber is required.", nameof(documentReferenceNumber));
        }
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("OriginalFileName is required.", nameof(originalFileName));
        }
        if (string.IsNullOrWhiteSpace(blobKey))
        {
            throw new ArgumentException("BlobKey is required.", nameof(blobKey));
        }
        if (fileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize), "FileSize must be greater than zero.");
        }
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("ContentType is required.", nameof(contentType));
        }
        if (string.IsNullOrWhiteSpace(uploadedByUserId))
        {
            throw new ArgumentException("UploadedByUserId is required.", nameof(uploadedByUserId));
        }
    }
}
