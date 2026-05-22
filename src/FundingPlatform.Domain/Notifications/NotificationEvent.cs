namespace FundingPlatform.Domain.Notifications;

/// <summary>
/// Spec 021 / T009 / FR-002 / Event Catalog v1 — closed set of email-notification
/// triggers. The string storage form (upper-snake-case) is what we persist in
/// <c>dbo.NotificationOutbox.EventType</c> so operators can read raw rows;
/// <see cref="NotificationEventExtensions.ToStorageString"/> is the canonical mapping.
///
/// <para>
/// <c>APPLICATION_SUBMITTED</c> is intentionally split into two enum values
/// (<see cref="ApplicationSubmittedReviewer"/> + <see cref="ApplicationSubmittedApplicant"/>)
/// so each outbox row carries one template variant and the idempotency key
/// dedupes cleanly per OQ-006.
/// </para>
/// </summary>
public enum NotificationEvent
{
    ApplicationSubmittedReviewer  = 1,
    ApplicationSubmittedApplicant = 2,
    ReturnedToApplicant           = 3,
    ResubmittedByApplicant        = 4,
    ApplicationApproved           = 5,
    ApplicationRejected           = 6,

    /// <summary>
    /// Spec 021 / US9 / FR-040 — an applicant withdrew an <c>UnderReview</c>
    /// Application. Notifies the same stage-group reviewer pool as
    /// <see cref="ApplicationSubmittedReviewer"/>.
    /// </summary>
    WithdrawnByApplicant          = 7,
}

/// <summary>
/// Spec 021 / T009 — string-storage helpers for <see cref="NotificationEvent"/>.
/// Keeps the enum operator-readable in dacpac rows while letting C# code use the
/// typed enum value. <see cref="FromStorageString"/> is the inverse used by EF.
/// </summary>
public static class NotificationEventExtensions
{
    /// <summary>
    /// Spec 021 / Event Catalog v1 — upper-snake-case canonical names used by
    /// <c>NotificationOutbox.EventType</c>, <c>NotificationDelivery.EventType</c>,
    /// and the idempotency-dedup unique index. Stable; never rename without a
    /// schema migration.
    /// </summary>
    public static string ToStorageString(this NotificationEvent value) => value switch
    {
        NotificationEvent.ApplicationSubmittedReviewer  => "APPLICATION_SUBMITTED_REVIEWER",
        NotificationEvent.ApplicationSubmittedApplicant => "APPLICATION_SUBMITTED_APPLICANT",
        NotificationEvent.ReturnedToApplicant           => "RETURNED_TO_APPLICANT",
        NotificationEvent.ResubmittedByApplicant        => "RESUBMITTED_BY_APPLICANT",
        NotificationEvent.ApplicationApproved           => "APPLICATION_APPROVED",
        NotificationEvent.ApplicationRejected           => "APPLICATION_REJECTED",
        NotificationEvent.WithdrawnByApplicant          => "APPLICATION_WITHDRAWN_BY_APPLICANT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown NotificationEvent")
    };

    /// <summary>
    /// Inverse of <see cref="ToStorageString"/>. Throws on unknown storage codes
    /// so a malformed row surfaces as a worker exception rather than a silent
    /// no-op (per NFR-004 the worker logs and continues).
    /// </summary>
    public static NotificationEvent FromStorageString(string storage) => storage switch
    {
        "APPLICATION_SUBMITTED_REVIEWER"  => NotificationEvent.ApplicationSubmittedReviewer,
        "APPLICATION_SUBMITTED_APPLICANT" => NotificationEvent.ApplicationSubmittedApplicant,
        "RETURNED_TO_APPLICANT"           => NotificationEvent.ReturnedToApplicant,
        "RESUBMITTED_BY_APPLICANT"        => NotificationEvent.ResubmittedByApplicant,
        "APPLICATION_APPROVED"            => NotificationEvent.ApplicationApproved,
        "APPLICATION_REJECTED"            => NotificationEvent.ApplicationRejected,
        "APPLICATION_WITHDRAWN_BY_APPLICANT" => NotificationEvent.WithdrawnByApplicant,
        _ => throw new ArgumentOutOfRangeException(nameof(storage), storage,
            "Unknown NotificationEvent storage code")
    };
}
