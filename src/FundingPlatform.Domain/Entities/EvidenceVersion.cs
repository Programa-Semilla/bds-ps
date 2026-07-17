namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 047 / FR-021 (research D4) — an immutable, append-only version of an
/// <see cref="Evidence"/> node. Each replace (of the file OR a reconciliation-critical field)
/// appends a new current row and marks the prior superseded (mirrors the <c>SignedUpload</c>
/// collection, NOT <c>DisbursementEvidence.Replace</c>'s in-place overwrite). Rows never mutate
/// except <see cref="IsCurrent"/> <c>1 → 0</c> when superseded. Exactly one current per evidence,
/// backed by the filtered unique <c>UX_EvidenceVersions_OneCurrent</c>.
/// </summary>
public sealed class EvidenceVersion
{
    public int Id { get; private set; }
    public int EvidenceId { get; private set; }
    public int VersionNumber { get; private set; }
    public bool IsCurrent { get; private set; }

    // File snapshot.
    public string BlobKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;

    // Reconciliation-critical field snapshot.
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = Evidence.RequiredCurrency;
    public string DocumentReferenceNumber { get; private set; } = string.Empty;
    public DateOnly DocumentDate { get; private set; }

    /// <summary>SHA-256 of the version's file (FR-021 integrity marker).</summary>
    public string FileHash { get; private set; } = string.Empty;

    /// <summary>Required for versions after the first (FR-021); null on the initial version.</summary>
    public string? Reason { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private EvidenceVersion() { } // EF

    /// <summary>Creates a version row. <c>internal</c> so <see cref="Evidence"/> (the aggregate root)
    /// is the single entry point — it assigns <see cref="VersionNumber"/> and the current flag.</summary>
    internal EvidenceVersion(
        int versionNumber,
        bool isCurrent,
        string blobKey,
        string originalFileName,
        long fileSize,
        string contentType,
        decimal amount,
        string currency,
        string documentReferenceNumber,
        DateOnly documentDate,
        string fileHash,
        string? reason,
        string createdByUserId)
    {
        VersionNumber = versionNumber;
        IsCurrent = isCurrent;
        BlobKey = blobKey;
        OriginalFileName = originalFileName;
        FileSize = fileSize;
        ContentType = contentType;
        Amount = amount;
        Currency = currency;
        DocumentReferenceNumber = documentReferenceNumber;
        DocumentDate = documentDate;
        FileHash = fileHash;
        Reason = reason;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The only permitted post-insert mutation: the current version is superseded when a
    /// replacement is appended (<see cref="Evidence.ReplaceCurrent"/>).</summary>
    internal void MarkSuperseded() => IsCurrent = false;
}
