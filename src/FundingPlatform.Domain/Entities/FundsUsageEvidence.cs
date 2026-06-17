using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 036 — one uploaded funds-usage evidence file on an application that has
/// reached <see cref="ApplicationState.AgreementExecuted"/>. A standalone aggregate
/// (own table, FK to Applications) rather than a navigation collection on the large
/// <see cref="Application"/> aggregate (research D2). The AgreementExecuted gate and
/// the ≤250-char note invariant live here (Constitution II — Rich Domain Model).
/// </summary>
public sealed class FundsUsageEvidence
{
    /// <summary>FR-006 — maximum note length.</summary>
    public const int MaxNoteLength = 250;

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string BlobKey { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public DateTime UploadedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private FundsUsageEvidence() { } // EF

    /// <summary>
    /// FR-001 — creation is only valid for an application in
    /// <see cref="ApplicationState.AgreementExecuted"/>. The application is passed in
    /// (a cheap tracked scalar load) so the invariant stays in the domain.
    /// </summary>
    public static FundsUsageEvidence CreateForExecutedApplication(
        Application application,
        string uploadedByUserId,
        string originalFileName,
        string blobKey,
        long fileSize,
        string contentType,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.State != ApplicationState.AgreementExecuted)
        {
            throw new InvalidOperationException(
                $"Funds-usage evidence can only be added to an application in {nameof(ApplicationState.AgreementExecuted)}; "
                + $"application {application.Id} is {application.State}.");
        }

        if (string.IsNullOrWhiteSpace(uploadedByUserId))
        {
            throw new ArgumentException("UploadedByUserId is required.", nameof(uploadedByUserId));
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

        return new FundsUsageEvidence
        {
            ApplicationId = application.Id,
            UploadedByUserId = uploadedByUserId,
            OriginalFileName = originalFileName.Trim(),
            BlobKey = blobKey,
            FileSize = fileSize,
            ContentType = contentType,
            Note = NormalizeNote(note),
            UploadedAt = DateTime.UtcNow,
        };
    }

    /// <summary>FR-006 — set/clear/change the note. Trims; empty → null; rejects &gt; 250.</summary>
    public void EditNote(string? note) => Note = NormalizeNote(note);

    private static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        if (trimmed.Length > MaxNoteLength)
        {
            throw new InvalidOperationException(
                $"Note exceeds the maximum length of {MaxNoteLength} characters.");
        }
        return trimmed;
    }
}
