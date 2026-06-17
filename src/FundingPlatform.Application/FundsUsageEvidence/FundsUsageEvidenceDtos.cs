namespace FundingPlatform.Application.FundsUsageEvidence;

/// <summary>Spec 036 — command to store one evidence file. The content stream is
/// already type-validated + size-bounded at the controller boundary.</summary>
public sealed record UploadFundsUsageEvidenceCommand(
    int ApplicationId,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    Stream Content,
    string? Note);

/// <summary>Spec 036 — flat read projection for the evidence list (newest-first).</summary>
public sealed record FundsUsageEvidenceListItem(
    int Id,
    string OriginalFileName,
    string? Note,
    string UploadedByDisplayName,
    DateTime UploadedAt,
    long FileSize,
    string ContentType);

/// <summary>Spec 036 — a resolved BackendStream serving handle for download.</summary>
public sealed record FundsUsageEvidenceDownload(
    Stream Content,
    string ContentType,
    string FileName);
