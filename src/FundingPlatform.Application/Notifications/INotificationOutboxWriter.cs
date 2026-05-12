using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Application.Notifications;

/// <summary>
/// Spec 021 / T015 / FR-001 — Application-Service-facing seam for the
/// transactional outbox. Called between
/// <c>application.AddVersionHistory(...)</c> and
/// <c>SaveChangesAsync()</c> so the outbox row lands in the SAME database
/// transaction as the workflow state change that triggered it. The writer
/// MUST NOT call <c>SaveChangesAsync</c> itself — it appends the entity to
/// the same pending unit-of-work the Application Service is about to commit.
///
/// <para>
/// A failed transaction (e.g., domain-level rule rejected by the aggregate
/// after the enqueue) leaves zero outbox rows on disk (FR-001).
/// </para>
/// </summary>
public interface INotificationOutboxWriter
{
    /// <summary>
    /// Append one outbox row to the current unit-of-work. The caller commits.
    /// </summary>
    Task EnqueueAsync(
        NotificationEvent eventType,
        int applicationId,
        int versionHistoryId,
        NotificationPayload payload,
        CancellationToken ct);

    /// <summary>
    /// Spec 021 / FR-007 / R-003 — true when at least one prior <c>VersionHistory</c>
    /// row with <c>Action="SendBack"</c> exists for this application. Drives the
    /// first-submit (false) vs resubmit (true) decision in <c>SubmitApplicationAsync</c>.
    /// </summary>
    Task<bool> HasPriorSendBackAsync(int applicationId, CancellationToken ct);

    /// <summary>
    /// Spec 021 / FR-007 — reads the applicant's group memberships. The result
    /// populates <see cref="NotificationPayload.StageGroupIds"/> so the resolver
    /// finds reviewers who share at least one group with the applicant
    /// (matches the spec-016 reviewer-scope predicate).
    /// </summary>
    Task<IReadOnlyList<int>> GetApplicantStageGroupIdsAsync(int applicationId, CancellationToken ct);
}
