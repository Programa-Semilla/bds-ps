namespace FundingPlatform.Domain.Entities;

public class Document
{
    public int Id { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;

    /// <summary>Spec 014 — canonical object-storage key for the uploaded file. Always populated.</summary>
    public string BlobKey { get; private set; } = string.Empty;

    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public DateTime UploadedAt { get; private set; }

    private Document() { }

    public Document(string originalFileName, string blobKey, long fileSize, string contentType)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new InvalidOperationException("Document requires a non-empty original file name.");
        if (string.IsNullOrWhiteSpace(blobKey))
            throw new InvalidOperationException("Document requires a non-empty blob key.");

        OriginalFileName = originalFileName;
        BlobKey = blobKey;
        FileSize = fileSize;
        ContentType = contentType;
        UploadedAt = DateTime.UtcNow;
    }
}
