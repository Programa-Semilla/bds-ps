using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 028 / US3 / T031 / SC-002 + SC-003 — signing-ceremony notification shapes:
/// regeneration re-fires AGREEMENT_GENERATED_APPLICANT against a fresh
/// VersionHistoryId (no dedup collapse, EC-003), and the signing reviewer-bucket
/// set equals the group-overlap inbox set (R-006).
/// </summary>
[TestFixture]
public class SigningNotificationsTests
{
    [Test]
    public async Task Regenerate_refires_agreement_generated_with_distinct_anchor()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        // Generate, then regenerate — two distinct VersionHistory rows, two emails.
        await h.EnqueueAsync(
            NotificationEvent.AgreementGeneratedApplicant, reviewerBucket: false,
            actorUserId: h.ParticipatingAdminUserId);
        await h.EnqueueAsync(
            NotificationEvent.AgreementGeneratedApplicant, reviewerBucket: false,
            actorUserId: h.ParticipatingAdminUserId);

        await h.RunWorkerAsync();

        var toApplicant = (await h.SentRecipientsAsync(NotificationEvent.AgreementGeneratedApplicant))
            .Count(id => id == h.ApplicantUserId);
        Assert.That(toApplicant, Is.EqualTo(2),
            "regeneration must re-fire AGREEMENT_GENERATED_APPLICANT (distinct VersionHistoryId).");
    }

    [Test]
    public async Task Signing_reviewer_bucket_equals_group_overlap_set()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        var recipients = await h.ResolveAsync(
            NotificationEvent.SignedUploadSubmittedReviewer,
            reviewerBucket: true, actorUserId: h.ApplicantUserId);

        var reviewerBucket = recipients
            .Where(r => r.Bucket == RecipientBucket.Reviewer)
            .Select(r => r.UserId)
            .ToArray();

        Assert.That(reviewerBucket, Is.EquivalentTo(new[] { h.ReviewerUserId }),
            "R-006: the signing reviewer set is the applicant↔reviewer group-overlap set.");
    }

    [Test]
    public async Task Reviewer_signing_events_notify_reviewer_not_applicant()
    {
        await using var h = await PostResolutionNotificationsHarness.CreateAsync();

        await h.EnqueueAsync(
            NotificationEvent.SignedUploadSubmittedReviewer, reviewerBucket: true,
            actorUserId: h.ApplicantUserId);

        await h.RunWorkerAsync();

        var sent = await h.SentRecipientsAsync(NotificationEvent.SignedUploadSubmittedReviewer);
        Assert.Multiple(() =>
        {
            Assert.That(sent, Does.Contain(h.ReviewerUserId));
            Assert.That(sent, Does.Contain(h.ParticipatingAdminUserId));
            Assert.That(sent, Does.Not.Contain(h.ApplicantUserId), "the uploading applicant (actor) is excluded.");
        });
    }
}
