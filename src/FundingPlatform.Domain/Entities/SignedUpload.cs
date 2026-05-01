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

    /// <summary>
    /// Spec 014 — canonical <c>ObjectKey</c> string for the signed PDF in the
    /// configured object storage backend. Always populated when the row is created.
    /// </summary>
    public string BlobKey { get; private set; } = string.Empty;

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
        string blobKey)
    {
        Validate(uploaderUserId, fileName, size, blobKey);

        FundingAgreementId = fundingAgreementId;
        UploaderUserId = uploaderUserId;
        GeneratedVersionAtUpload = generatedVersionAtUpload;
        FileName = fileName;
        Size = size;
        BlobKey = blobKey;
        UploadedAtUtc = DateTime.UtcNow;
        Status = SignedUploadStatus.Pending;
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

    private static void Validate(string uploaderUserId, string fileName, long size, string blobKey)
    {
        if (string.IsNullOrWhiteSpace(uploaderUserId))
            throw new InvalidOperationException("SignedUpload requires a non-empty uploader user id.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("SignedUpload requires a non-empty file name.");
        if (size <= 0)
            throw new InvalidOperationException("SignedUpload size must be greater than zero.");
        if (string.IsNullOrWhiteSpace(blobKey))
            throw new InvalidOperationException("SignedUpload requires a non-empty blob key.");
    }
}
