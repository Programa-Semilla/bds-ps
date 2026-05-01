namespace FundingPlatform.Domain.Entities;

public class Document
{
    public int Id { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoragePath { get; private set; } = string.Empty;

    /// <summary>Spec 014 / T052 — canonical object-storage key for the uploaded file.</summary>
    public string? BlobKey { get; private set; }

    /// <summary>Spec 014 / T052 — pre-014 absolute filesystem path (backfilled).</summary>
    public string? LegacyPath { get; private set; }

    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public DateTime UploadedAt { get; private set; }

    private Document() { }

    public Document(string originalFileName, string storagePath, long fileSize, string contentType)
    {
        OriginalFileName = originalFileName;
        StoragePath = storagePath;
        FileSize = fileSize;
        ContentType = contentType;
        UploadedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 014 / T052 — record the canonical object-storage key for the document.
    /// Behavior method per Constitution Principle II.
    /// </summary>
    public void RecordBlob(string blobKey)
    {
        if (string.IsNullOrWhiteSpace(blobKey))
            throw new InvalidOperationException("RecordBlob requires a non-empty blob key.");
        BlobKey = blobKey;
    }
}
