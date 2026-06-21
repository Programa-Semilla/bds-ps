using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 041 / US2 / T022 / FR-011 — the "Solicitud en revisión" applicant notice.
/// Asserts the recipient matrix is applicant-only (reviewer + both admins excluded)
/// and that the outbox dedup key keeps a reviewer re-opening the page from sending
/// a duplicate.
/// </summary>
[TestFixture]
public class UnderReviewNotificationsTests
{
    [Test]
    public async Task UnderReview_resolves_to_applicant_only()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        // The reviewer is the actor (excluded anyway); the event is applicant-only.
        var recipients = await h.ResolveAsync(
            NotificationEvent.ApplicationUnderReviewApplicant, reviewerBucket: false,
            actorUserId: h.ReviewerUserId);
        var ids = recipients.Select(r => r.UserId).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.EquivalentTo(new[] { h.ApplicantUserId }),
                "FR-011: the under-review notice goes to the applicant only.");
            Assert.That(ids, Does.Not.Contain(h.ReviewerUserId), "reviewer excluded.");
            Assert.That(ids, Does.Not.Contain(h.ParticipatingAdminUserId), "participating admin excluded.");
            Assert.That(ids, Does.Not.Contain(h.NonParticipatingAdminUserId), "non-participating admin excluded.");
        });
    }

    [Test]
    public async Task UnderReview_dedup_key_prevents_duplicate_on_reopen()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        // A reviewer re-opening the page is guarded at the service level (the
        // Submitted-state check), but the outbox dedup key
        // (EventType, ApplicationId, VersionHistoryId, RecipientUserId) is the
        // backstop: two rows on the same transition collapse to one send.
        var vhId = await h.EnqueueAsync(
            NotificationEvent.ApplicationUnderReviewApplicant, reviewerBucket: false,
            actorUserId: h.ReviewerUserId);
        await h.EnqueueAsync(
            NotificationEvent.ApplicationUnderReviewApplicant, reviewerBucket: false,
            actorUserId: h.ReviewerUserId, versionHistoryId: vhId);

        await h.RunWorkerAsync();

        var toApplicant = (await h.SentRecipientsAsync(NotificationEvent.ApplicationUnderReviewApplicant))
            .Count(id => id == h.ApplicantUserId);
        Assert.That(toApplicant, Is.EqualTo(1),
            "exactly one under-review email to the applicant, even across a duplicate enqueue.");
    }
}
