namespace FundingPlatform.Application.FundsUsageEvidence;

/// <summary>
/// Spec 036 — orchestrates funds-usage evidence list/upload/edit-note/delete/download.
/// The caller (controller) owns group-scope + role authorization and runs the
/// size guard + file-type policy at the HTTP boundary; this service trusts the
/// caller for scope, exactly like the existing Fund/Process services.
/// </summary>
public interface IFundsUsageEvidenceService
{
    /// <summary>Flat, group-agnostic read ordered newest-first. Authorization is the controller's job.</summary>
    Task<IReadOnlyList<FundsUsageEvidenceListItem>> ListAsync(int applicationId, CancellationToken ct);

    /// <summary>Validates the application is AgreementExecuted (domain factory), stores the
    /// blob, persists the row + audit in one transaction. Returns the created id.</summary>
    Task<int> UploadAsync(UploadFundsUsageEvidenceCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>Set/clear/change the ≤250-char note + audit. Throws <see cref="KeyNotFoundException"/>
    /// when the row is missing (controller maps to NotFound()).</summary>
    Task EditNoteAsync(int evidenceId, string? note, string actorUserId, CancellationToken ct);

    /// <summary>Deletes the blob then the row + audit. Idempotent: a missing row throws
    /// <see cref="KeyNotFoundException"/> (controller maps to NotFound()).</summary>
    Task DeleteAsync(int evidenceId, string actorUserId, CancellationToken ct);

    /// <summary>Resolves a BackendStream serving handle for download, or null when missing.</summary>
    Task<FundsUsageEvidenceDownload?> OpenForDownloadAsync(int evidenceId, CancellationToken ct);
}
