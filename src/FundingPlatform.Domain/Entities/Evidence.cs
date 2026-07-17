using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 047 (research D1/D4) — a typed evidence-graph node: a supporting document (bank receipt,
/// invoice, signed acceptance, credit note, refund receipt, other) attached to an executed
/// application and linked M:N to budget-lines via <see cref="EvidenceLineAllocation"/>. A standalone
/// Application-scoped aggregate that lives ALONGSIDE the untouched P1 <see cref="DisbursementEvidence"/>
/// money-gate (D1) — a third evidence aggregate, idiomatic here like <c>FundsUsageEvidence</c>.
///
/// The row carries the CURRENT denormalized file + reconciliation-critical values (for query/
/// reconciliation); the append-only <see cref="EvidenceVersion"/> chain is the audit history (D4).
/// The orphan guard (must link ≥1 line or a disbursement) and the allocation-integrity /
/// closure-lock gates live in the service (they span other aggregates); CRC-only + positive amount
/// are enforced here (Constitution II).
/// </summary>
public sealed class Evidence
{
    /// <summary>P3 accepts only the platform base currency (FR-026 — currency deferred to P5).</summary>
    public const string RequiredCurrency = "CRC";

    private readonly List<EvidenceVersion> _versions = [];

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    /// <summary>Optional payment anchor (supplementary receipt/invoice); null for acceptance/credit-note/refund/other.</summary>
    public int? DisbursementId { get; private set; }
    public EvidenceType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = RequiredCurrency;
    public string DocumentReferenceNumber { get; private set; } = string.Empty;
    public DateOnly DocumentDate { get; private set; }
    public int? SupplierId { get; private set; }

    // Denormalized current-file pointer (mirrors the current EvidenceVersion).
    public string BlobKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    /// <summary>SHA-256 of the current file (FR-021).</summary>
    public string FileHash { get; private set; } = string.Empty;

    public string UploadedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<EvidenceVersion> Versions => _versions.AsReadOnly();

    private Evidence() { } // EF

    /// <summary>
    /// FR-002/FR-021 — attach a new evidence document with its initial version (v1, current, no
    /// reason). Validates CRC + positive amount + required fields. The executed-application gate and
    /// the orphan/allocation-integrity checks are enforced by the service; the file metadata is
    /// type-validated + size-bounded at the controller boundary (FR-049).
    /// </summary>
    public static Evidence Attach(
        int applicationId,
        EvidenceType type,
        int? disbursementId,
        decimal amount,
        string currency,
        string documentReferenceNumber,
        DateOnly documentDate,
        int? supplierId,
        string originalFileName,
        string blobKey,
        long fileSize,
        string contentType,
        string fileHash,
        string uploadedByUserId)
    {
        Guard(amount, currency, documentReferenceNumber, originalFileName, blobKey, fileSize, contentType, fileHash, uploadedByUserId);

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var evidence = new Evidence
        {
            ApplicationId = applicationId,
            Type = type,
            DisbursementId = disbursementId,
            Amount = amount,
            Currency = normalizedCurrency,
            DocumentReferenceNumber = documentReferenceNumber.Trim(),
            DocumentDate = documentDate,
            SupplierId = supplierId,
            BlobKey = blobKey,
            OriginalFileName = originalFileName.Trim(),
            FileSize = fileSize,
            ContentType = contentType,
            FileHash = fileHash,
            UploadedByUserId = uploadedByUserId,
            UploadedAtUtc = DateTimeOffset.UtcNow,
        };

        evidence._versions.Add(new EvidenceVersion(
            versionNumber: 1,
            isCurrent: true,
            blobKey: blobKey,
            originalFileName: evidence.OriginalFileName,
            fileSize: fileSize,
            contentType: contentType,
            amount: amount,
            currency: normalizedCurrency,
            documentReferenceNumber: evidence.DocumentReferenceNumber,
            documentDate: documentDate,
            fileHash: fileHash,
            reason: null,
            createdByUserId: uploadedByUserId));

        return evidence;
    }

    /// <summary>
    /// FR-021 (research D4) — append a new current version (file replace AND/OR a reconciliation-
    /// critical field edit), superseding the prior. A non-empty <paramref name="reason"/> is required.
    /// Updates the denormalized current values on the row. When the file is unchanged the caller
    /// passes the existing blob pointer + hash.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the reason is blank or a required field is invalid.</exception>
    public void ReplaceCurrent(
        decimal amount,
        string currency,
        string documentReferenceNumber,
        DateOnly documentDate,
        string originalFileName,
        string blobKey,
        long fileSize,
        string contentType,
        string fileHash,
        string reason,
        string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to append a new evidence version.", nameof(reason));
        }
        Guard(amount, currency, documentReferenceNumber, originalFileName, blobKey, fileSize, contentType, fileHash, actorUserId);

        foreach (var current in _versions.Where(v => v.IsCurrent).ToList())
        {
            current.MarkSuperseded();
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var nextNumber = _versions.Count == 0 ? 1 : _versions.Max(v => v.VersionNumber) + 1;
        _versions.Add(new EvidenceVersion(
            versionNumber: nextNumber,
            isCurrent: true,
            blobKey: blobKey,
            originalFileName: originalFileName.Trim(),
            fileSize: fileSize,
            contentType: contentType,
            amount: amount,
            currency: normalizedCurrency,
            documentReferenceNumber: documentReferenceNumber.Trim(),
            documentDate: documentDate,
            fileHash: fileHash,
            reason: reason.Trim(),
            createdByUserId: actorUserId));

        Amount = amount;
        Currency = normalizedCurrency;
        DocumentReferenceNumber = documentReferenceNumber.Trim();
        DocumentDate = documentDate;
        OriginalFileName = originalFileName.Trim();
        BlobKey = blobKey;
        FileSize = fileSize;
        ContentType = contentType;
        FileHash = fileHash;
        UploadedByUserId = actorUserId;
        UploadedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void Guard(
        decimal amount,
        string currency,
        string documentReferenceNumber,
        string originalFileName,
        string blobKey,
        long fileSize,
        string contentType,
        string fileHash,
        string userId)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Evidence amount must be greater than zero.");
        }
        if (!string.Equals(currency?.Trim(), RequiredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Only {RequiredCurrency} is accepted in this slice (FR-026); got '{currency}'.");
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
        if (string.IsNullOrWhiteSpace(fileHash))
        {
            throw new ArgumentException("FileHash is required.", nameof(fileHash));
        }
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }
    }
}
