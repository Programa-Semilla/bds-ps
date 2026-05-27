using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 028 / US2 / T022 / SC-002 + SC-003 — appeal-lifecycle notification shapes
/// the original six events never had: a bidirectional conversation and a dual-fire
/// resolution. Validates dual-fire (two distinct emails), no idempotency collapse
/// across successive messages (distinct VersionHistoryId), and message direction.
/// </summary>
[TestFixture]
public class AppealNotificationsTests
{
    [Test]
    public async Task GrantReopenToReview_dual_fire_yields_two_distinct_emails()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        // Both rows share one VersionHistoryId; the resolving admin is the actor
        // (excluded), so recipients reduce cleanly to the applicant + the reviewer.
        var vhId = await h.EnqueueAsync(
            NotificationEvent.AppealResolvedApplicant, reviewerBucket: false,
            actorUserId: h.ParticipatingAdminUserId, outcomeCode: "AppealReopenedToReview");
        await h.EnqueueAsync(
            NotificationEvent.AppealReopenedReviewer, reviewerBucket: true,
            actorUserId: h.ParticipatingAdminUserId, versionHistoryId: vhId);

        await h.RunWorkerAsync();

        var resolved = await h.SentRecipientsAsync(NotificationEvent.AppealResolvedApplicant);
        var reopened = await h.SentRecipientsAsync(NotificationEvent.AppealReopenedReviewer);
        var total = await h.Db.NotificationDeliveries.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.EquivalentTo(new[] { h.ApplicantUserId }),
                "the resolution notifies the applicant.");
            Assert.That(reopened, Is.EquivalentTo(new[] { h.ReviewerUserId }),
                "the reopen-to-review notifies the reviewer.");
            Assert.That(total, Is.EqualTo(2),
                "FR-006: exactly two distinct emails (same VersionHistoryId, distinct EventType).");
        });
    }

    [Test]
    public async Task Three_successive_applicant_messages_yield_three_reviewer_emails()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        // Each posted message anchors on its own VersionHistory row → the
        // idempotency index must NOT collapse them (EC-002).
        for (var i = 0; i < 3; i++)
        {
            await h.EnqueueAsync(
                NotificationEvent.AppealMessageReviewer, reviewerBucket: true,
                actorUserId: h.ApplicantUserId);
        }

        await h.RunWorkerAsync();

        var toReviewer = (await h.SentRecipientsAsync(NotificationEvent.AppealMessageReviewer))
            .Count(id => id == h.ReviewerUserId);
        Assert.That(toReviewer, Is.EqualTo(3),
            "three distinct messages must produce three reviewer emails (no dedup collapse).");
    }

    [Test]
    public async Task Message_direction_follows_the_author()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        // Applicant authored → reviewer bucket.
        var toReviewers = await h.ResolveAsync(
            NotificationEvent.AppealMessageReviewer, reviewerBucket: true, actorUserId: h.ApplicantUserId);
        // Reviewer authored → applicant bucket.
        var toApplicant = await h.ResolveAsync(
            NotificationEvent.AppealMessageApplicant, reviewerBucket: false, actorUserId: h.ReviewerUserId);

        Assert.Multiple(() =>
        {
            Assert.That(toReviewers.Select(r => r.UserId), Does.Contain(h.ReviewerUserId));
            Assert.That(toReviewers.Select(r => r.UserId), Does.Not.Contain(h.ApplicantUserId));
            Assert.That(toApplicant.Select(r => r.UserId), Does.Contain(h.ApplicantUserId));
            Assert.That(toApplicant.Select(r => r.UserId), Does.Not.Contain(h.ReviewerUserId));
        });
    }
}
