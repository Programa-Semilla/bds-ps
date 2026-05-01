using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

public class FundingAgreement
{
    private readonly List<SignedUpload> _signedUploads = [];

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Size { get; private set; }

    /// <summary>Spec 014 — canonical object-storage key for the generated PDF. Always populated.</summary>
    public string BlobKey { get; private set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; private set; }
    public string GeneratedByUserId { get; private set; } = string.Empty;
    public int GeneratedVersion { get; private set; } = 1;
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<SignedUpload> SignedUploads => _signedUploads.AsReadOnly();

    public bool IsLocked => _signedUploads.Count > 0;

    public SignedUpload? PendingUpload =>
        _signedUploads.SingleOrDefault(u => u.Status == SignedUploadStatus.Pending);

    private FundingAgreement() { }

    internal FundingAgreement(
        int applicationId,
        string fileName,
        string contentType,
        long size,
        string blobKey,
        string generatedByUserId)
    {
        Validate(fileName, contentType, size, blobKey, generatedByUserId);

        ApplicationId = applicationId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        BlobKey = blobKey;
        GeneratedAtUtc = DateTime.UtcNow;
        GeneratedByUserId = generatedByUserId;
        GeneratedVersion = 1;
    }

    internal void Replace(
        string fileName,
        string contentType,
        long size,
        string blobKey,
        string regeneratingUserId)
    {
        if (IsLocked)
            throw new InvalidOperationException(
                "Funding agreement is locked: a signed upload has been submitted.");

        Validate(fileName, contentType, size, blobKey, regeneratingUserId);

        FileName = fileName;
        ContentType = contentType;
        Size = size;
        BlobKey = blobKey;
        GeneratedAtUtc = DateTime.UtcNow;
        GeneratedByUserId = regeneratingUserId;
        GeneratedVersion++;
    }

    internal SignedUpload AcceptSignedUpload(
        string uploaderUserId,
        int generatedVersionAtUpload,
        string fileName,
        long size,
        string blobKey)
    {
        if (PendingUpload is not null)
            throw new InvalidOperationException(
                "A pending signed upload already exists; replace it instead.");

        if (generatedVersionAtUpload != GeneratedVersion)
            throw new InvalidOperationException(
                "Signed upload references a superseded agreement version; please re-download the latest agreement.");

        var upload = new SignedUpload(
            Id, uploaderUserId, generatedVersionAtUpload, fileName, size, blobKey);
        _signedUploads.Add(upload);
        return upload;
    }

    internal SignedUpload ReplacePendingUpload(
        string uploaderUserId,
        int generatedVersionAtUpload,
        string fileName,
        long size,
        string blobKey)
    {
        var pending = PendingUpload
            ?? throw new InvalidOperationException("No pending signed upload to replace.");

        if (generatedVersionAtUpload != GeneratedVersion)
            throw new InvalidOperationException(
                "Signed upload references a superseded agreement version; please re-download the latest agreement.");

        pending.MarkSuperseded();

        var upload = new SignedUpload(
            Id, uploaderUserId, generatedVersionAtUpload, fileName, size, blobKey);
        _signedUploads.Add(upload);
        return upload;
    }

    internal void WithdrawPendingUpload(string withdrawingUserId)
    {
        if (string.IsNullOrWhiteSpace(withdrawingUserId))
            throw new InvalidOperationException("Withdrawing user id must be non-empty.");

        var pending = PendingUpload
            ?? throw new InvalidOperationException("No pending signed upload to withdraw.");

        pending.MarkWithdrawn();
    }

    internal SigningReviewDecision ApprovePendingUpload(string reviewerUserId, string? comment)
    {
        var pending = PendingUpload
            ?? throw new InvalidOperationException("No pending signed upload to approve.");

        return pending.Approve(reviewerUserId, comment);
    }

    internal SigningReviewDecision RejectPendingUpload(string reviewerUserId, string comment)
    {
        var pending = PendingUpload
            ?? throw new InvalidOperationException("No pending signed upload to reject.");

        return pending.Reject(reviewerUserId, comment);
    }

    private static void Validate(
        string fileName,
        string contentType,
        long size,
        string blobKey,
        string userId)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("FundingAgreement requires a non-empty file name.");

        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"FundingAgreement content type must be 'application/pdf' (was '{contentType}').");

        if (size <= 0)
            throw new InvalidOperationException("FundingAgreement size must be greater than zero.");

        if (string.IsNullOrWhiteSpace(blobKey))
            throw new InvalidOperationException("FundingAgreement requires a non-empty blob key.");

        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("FundingAgreement requires a non-empty generating user id.");
    }
}
