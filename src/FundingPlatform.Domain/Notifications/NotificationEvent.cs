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

    // ---------------------------------------------------------------------
    // Spec 028 — post-resolution notifications (12 additive events closing the
    // gap after an application reaches Resolved). String-stored, so the ordinals
    // are cosmetic. Recipients are counterparty-only (the actor never self-confirms).
    // ---------------------------------------------------------------------

    /// <summary>Spec 028 / US1 / FR-001 — applicant submitted accept/reject
    /// decisions on the resolution; notifies the stage-group reviewers + admins.
    /// Closes the reported bug (reviewer not notified on applicant response).</summary>
    ResponseSubmittedReviewer     = 8,

    /// <summary>Spec 028 / US2 / FR-002 — applicant opened an appeal; notifies reviewers + admins.</summary>
    AppealOpenedReviewer          = 9,

    /// <summary>Spec 028 / US2 / FR-003 — applicant posted an appeal message; notifies reviewers + admins.</summary>
    AppealMessageReviewer         = 10,

    /// <summary>Spec 028 / US2 / FR-004 — reviewer posted an appeal message; notifies the applicant + admins.</summary>
    AppealMessageApplicant        = 11,

    /// <summary>Spec 028 / US2 / FR-005 — appeal resolved (uphold / reopen-draft /
    /// reopen-review); notifies the applicant + admins. Body switches on OutcomeCode.</summary>
    AppealResolvedApplicant       = 12,

    /// <summary>Spec 028 / US2 / FR-006 — appeal resolved as GrantReopenToReview;
    /// dual-fires with <see cref="AppealResolvedApplicant"/> to notify reviewers + admins.</summary>
    AppealReopenedReviewer        = 13,

    /// <summary>Spec 028 / US3 / FR-010 — convenio generated/regenerated; notifies the applicant + admins.</summary>
    AgreementGeneratedApplicant   = 14,

    /// <summary>Spec 028 / US3 / FR-007 — applicant uploaded a signed convenio; notifies reviewers + admins.</summary>
    SignedUploadSubmittedReviewer = 15,

    /// <summary>Spec 028 / US3 / FR-008 — applicant replaced the pending signed upload; notifies reviewers + admins.</summary>
    SignedUploadReplacedReviewer  = 16,

    /// <summary>Spec 028 / US3 / FR-009 — applicant withdrew the pending signed upload; notifies reviewers + admins.</summary>
    SignedUploadWithdrawnReviewer = 17,

    /// <summary>Spec 028 / US3 / FR-011 — reviewer approved the signed convenio (executed); notifies the applicant + admins.</summary>
    AgreementExecutedApplicant    = 18,

    /// <summary>Spec 028 / US3 / FR-012 — reviewer rejected the signed convenio (changes required); notifies the applicant + admins.</summary>
    SignedUploadRejectedApplicant = 19,

    /// <summary>Spec 041 / US2 / FR-011 — a reviewer began review (Submitted → UnderReview);
    /// notifies the applicant only. Distinct from the submission receipt (OQ-2). Fired at the
    /// real <see cref="Domain.Entities.Application.StartReview"/> transition in ReviewService.</summary>
    ApplicationUnderReviewApplicant = 20,
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
        // Spec 028 — post-resolution events.
        NotificationEvent.ResponseSubmittedReviewer     => "RESPONSE_SUBMITTED_REVIEWER",
        NotificationEvent.AppealOpenedReviewer          => "APPEAL_OPENED_REVIEWER",
        NotificationEvent.AppealMessageReviewer         => "APPEAL_MESSAGE_REVIEWER",
        NotificationEvent.AppealMessageApplicant        => "APPEAL_MESSAGE_APPLICANT",
        NotificationEvent.AppealResolvedApplicant       => "APPEAL_RESOLVED_APPLICANT",
        NotificationEvent.AppealReopenedReviewer        => "APPEAL_REOPENED_REVIEWER",
        NotificationEvent.AgreementGeneratedApplicant   => "AGREEMENT_GENERATED_APPLICANT",
        NotificationEvent.SignedUploadSubmittedReviewer => "SIGNED_UPLOAD_SUBMITTED_REVIEWER",
        NotificationEvent.SignedUploadReplacedReviewer  => "SIGNED_UPLOAD_REPLACED_REVIEWER",
        NotificationEvent.SignedUploadWithdrawnReviewer => "SIGNED_UPLOAD_WITHDRAWN_REVIEWER",
        NotificationEvent.AgreementExecutedApplicant    => "AGREEMENT_EXECUTED_APPLICANT",
        NotificationEvent.SignedUploadRejectedApplicant => "SIGNED_UPLOAD_REJECTED_APPLICANT",
        // Spec 041 — applicant under-review notice.
        NotificationEvent.ApplicationUnderReviewApplicant => "APPLICATION_UNDER_REVIEW_APPLICANT",
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
        // Spec 028 — post-resolution events.
        "RESPONSE_SUBMITTED_REVIEWER"     => NotificationEvent.ResponseSubmittedReviewer,
        "APPEAL_OPENED_REVIEWER"          => NotificationEvent.AppealOpenedReviewer,
        "APPEAL_MESSAGE_REVIEWER"         => NotificationEvent.AppealMessageReviewer,
        "APPEAL_MESSAGE_APPLICANT"        => NotificationEvent.AppealMessageApplicant,
        "APPEAL_RESOLVED_APPLICANT"       => NotificationEvent.AppealResolvedApplicant,
        "APPEAL_REOPENED_REVIEWER"        => NotificationEvent.AppealReopenedReviewer,
        "AGREEMENT_GENERATED_APPLICANT"   => NotificationEvent.AgreementGeneratedApplicant,
        "SIGNED_UPLOAD_SUBMITTED_REVIEWER" => NotificationEvent.SignedUploadSubmittedReviewer,
        "SIGNED_UPLOAD_REPLACED_REVIEWER" => NotificationEvent.SignedUploadReplacedReviewer,
        "SIGNED_UPLOAD_WITHDRAWN_REVIEWER" => NotificationEvent.SignedUploadWithdrawnReviewer,
        "AGREEMENT_EXECUTED_APPLICANT"    => NotificationEvent.AgreementExecutedApplicant,
        "SIGNED_UPLOAD_REJECTED_APPLICANT" => NotificationEvent.SignedUploadRejectedApplicant,
        "APPLICATION_UNDER_REVIEW_APPLICANT" => NotificationEvent.ApplicationUnderReviewApplicant,
        _ => throw new ArgumentOutOfRangeException(nameof(storage), storage,
            "Unknown NotificationEvent storage code")
    };
}
