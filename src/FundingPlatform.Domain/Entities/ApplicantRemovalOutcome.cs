using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 021 / US9 — classifies what an applicant-initiated removal did, so the
/// Application layer can phrase the right confirmation message and decide whether
/// to enqueue the reviewer notification, without re-inspecting the Application's
/// pre-removal state (Constitution II — the domain owns the decision).
/// </summary>
public enum ApplicantRemovalKind
{
    /// <summary>A <c>Draft</c> was soft-deleted.</summary>
    DraftDeleted,

    /// <summary>A <c>Submitted</c> or <c>UnderReview</c> Application was soft-deleted.</summary>
    Withdrawn,

    /// <summary>The Application was already soft-deleted; the call was a no-op.</summary>
    NoOp,
}

/// <summary>
/// Spec 021 / US9 / FR-040 — result of <see cref="Application.RemoveByApplicant"/>.
/// <see cref="NotifyReviewers"/> is true only when an <c>UnderReview</c> Application
/// was withdrawn; it is the single signal the Application layer uses to enqueue the
/// <c>APPLICATION_WITHDRAWN_BY_APPLICANT</c> notification.
/// </summary>
public readonly record struct ApplicantRemovalOutcome(
    ApplicantRemovalKind Kind,
    bool NotifyReviewers,
    ApplicationState PriorState);
