using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 028 / US1 / T013 / SC-002 + SC-003 / SC-007 — RESPONSE_SUBMITTED_REVIEWER
/// recipient matrix and idempotency. The applicant responding to the resolution
/// notifies the stage-group reviewers + participating admins, never the applicant
/// nor a non-participating admin (closes the reported bug).
/// </summary>
[TestFixture]
public class ResponseSubmittedNotificationsTests
{
    [Test]
    public async Task Recipient_matrix_is_reviewers_in_group_plus_participating_admin_only()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        var recipients = await h.ResolveAsync(
            NotificationEvent.ResponseSubmittedReviewer,
            reviewerBucket: true,
            actorUserId: h.ApplicantUserId);

        var ids = recipients.Select(r => r.UserId).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(h.ReviewerUserId), "stage-group reviewer must be notified.");
            Assert.That(ids, Does.Contain(h.ParticipatingAdminUserId), "participating admin must be notified.");
            Assert.That(ids, Does.Not.Contain(h.ApplicantUserId), "the applicant (actor) must NOT be notified (SC-007).");
            Assert.That(ids, Does.Not.Contain(h.NonParticipatingAdminUserId), "a non-participating admin must NOT be notified.");
        });
    }

    [Test]
    public async Task Worker_sends_once_per_recipient_and_is_idempotent_on_second_pass()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        await h.EnqueueAsync(
            NotificationEvent.ResponseSubmittedReviewer,
            reviewerBucket: true, actorUserId: h.ApplicantUserId);

        await h.RunWorkerAsync();

        var afterFirst = await h.SentRecipientsAsync(NotificationEvent.ResponseSubmittedReviewer);
        Assert.That(afterFirst, Is.EquivalentTo(new[] { h.ReviewerUserId, h.ParticipatingAdminUserId }),
            "first pass: one Sent delivery to reviewer + participating admin.");
        var sentAfterFirst = h.SentCount;

        // Force the outbox row back to Pending and re-run — the idempotency index
        // must prevent duplicate deliveries and provider calls (SC-003).
        var row = await h.Db.NotificationOutbox
            .FirstAsync(o => o.EventType == NotificationEvent.ResponseSubmittedReviewer.ToStorageString());
        row.MarkTransientFailure("simulated", DateTime.UtcNow.AddSeconds(-1));
        await h.Db.SaveChangesAsync();

        await h.RunWorkerAsync();

        var afterSecond = await h.SentRecipientsAsync(NotificationEvent.ResponseSubmittedReviewer);
        Assert.That(afterSecond, Has.Count.EqualTo(afterFirst.Count),
            "SC-003: second pass adds no duplicate NotificationDelivery rows.");
        Assert.That(h.SentCount, Is.EqualTo(sentAfterFirst),
            "SC-003: second pass makes no additional provider calls.");
    }
}
