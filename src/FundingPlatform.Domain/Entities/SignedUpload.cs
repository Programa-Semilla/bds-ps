using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

public class SignedUpload
{
    public int Id { get; private set; }
    public int FundingAgreementId { get; private set; }
    public string UploaderUserId { get; private set; } = string.Empty;
    public int GeneratedVersionAtUpload { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "application/pdf";
    public long Size { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;

    /// <summary>
    /// Spec 014 / T029 — canonical <c>ObjectKey</c> string for the signed PDF in the
    /// configured object storage backend. Nullable while the migration is in flight;
    /// once <see cref="LegacyPath"/> is null and <see cref="BlobKey"/> is non-null,
    /// the row has been fully migrated. New rows set this on creation.
    /// </summary>
    public string? BlobKey { get; private set; }

    /// <summary>
    /// Spec 014 / T029 — pre-014 absolute filesystem path. Populated by the
    /// post-deploy backfill script for rows that existed before the migration.
    /// </summary>
    public string? LegacyPath { get; private set; }

    public DateTime UploadedAtUtc { get; private set; }
    public SignedUploadStatus Status { get; private set; } = SignedUploadStatus.Pending;
    public byte[] RowVersion { get; private set; } = [];

    private SigningReviewDecision? _reviewDecision;
    public SigningReviewDecision? ReviewDecision => _reviewDecision;

    private SignedUpload() { }

    internal SignedUpload(
        int fundingAgreementId,
        string uploaderUserId,
        int generatedVersionAtUpload,
        string fileName,
        long size,
        string storagePath)
    {
        Validate(uploaderUserId, fileName, size, storagePath);

        FundingAgreementId = fundingAgreementId;
        UploaderUserId = uploaderUserId;
        GeneratedVersionAtUpload = generatedVersionAtUpload;
        FileName = fileName;
        Size = size;
        StoragePath = storagePath;
        UploadedAtUtc = DateTime.UtcNow;
        Status = SignedUploadStatus.Pending;
    }

    /// <summary>
    /// Spec 014 / T029 — record the canonical object-storage key for this signed upload.
    /// Behavior method (Constitution Principle II) so the column is encapsulated rather
    /// than set via a public setter. The key is validated by the value object before being
    /// flattened to its string form for persistence; we accept the value object so callers
    /// cannot smuggle an arbitrary string in.
    /// </summary>
    public void RecordBlob(string blobKey)
    {
        if (string.IsNullOrWhiteSpace(blobKey))
            throw new InvalidOperationException("RecordBlob requires a non-empty blob key.");
        BlobKey = blobKey;
    }

    internal void MarkSuperseded() => Transition(SignedUploadStatus.Superseded);

    internal void MarkWithdrawn() => Transition(SignedUploadStatus.Withdrawn);

    internal SigningReviewDecision Reject(string reviewerUserId, string comment)
    {
        Transition(SignedUploadStatus.Rejected);
        _reviewDecision = new SigningReviewDecision(Id, SigningDecisionOutcome.Rejected, reviewerUserId, comment);
        return _reviewDecision;
    }

    internal SigningReviewDecision Approve(string reviewerUserId, string? comment)
    {
        Transition(SignedUploadStatus.Approved);
        _reviewDecision = new SigningReviewDecision(Id, SigningDecisionOutcome.Approved, reviewerUserId, comment);
        return _reviewDecision;
    }

    private void Transition(SignedUploadStatus target)
    {
        if (Status != SignedUploadStatus.Pending)
            throw new InvalidOperationException(
                $"SignedUpload {Id} cannot transition to {target}: current status is {Status}.");
        Status = target;
    }

    private static void Validate(string uploaderUserId, string fileName, long size, string storagePath)
    {
        if (string.IsNullOrWhiteSpace(uploaderUserId))
            throw new InvalidOperationException("SignedUpload requires a non-empty uploader user id.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("SignedUpload requires a non-empty file name.");
        if (size <= 0)
            throw new InvalidOperationException("SignedUpload size must be greater than zero.");
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new InvalidOperationException("SignedUpload requires a non-empty storage path.");
    }
}
