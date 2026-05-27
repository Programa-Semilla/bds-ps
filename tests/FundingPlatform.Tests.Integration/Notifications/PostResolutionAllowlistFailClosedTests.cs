using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 028 / T034 / SC-004 / FR-017 — the non-prod allowlist must stay fail-closed
/// for the new post-resolution events too: an empty allowlist blocks every recipient
/// (zero provider calls, one BlockedByAllowlist delivery row). Sampled across the
/// applicant-bucket events (the shared harness seeds only the applicant recipient).
/// </summary>
[TestFixture]
public class PostResolutionAllowlistFailClosedTests
{
    [TestCase(NotificationEvent.AgreementGeneratedApplicant)]
    [TestCase(NotificationEvent.AppealResolvedApplicant)]
    [TestCase(NotificationEvent.SignedUploadRejectedApplicant)]
    public async Task Empty_allowlist_blocks_new_applicant_event(NotificationEvent ev)
    {
        var sendInvocations = 0;
        await using var h = await NotificationsTestHarness.CreateAsync(
            senderOutcome: EmailSendOutcome.Sent,
            allowlistEntry: string.Empty,
            onSend: _ => Interlocked.Increment(ref sendInvocations));

        await h.SeedApplicationAsync();
        await h.EnqueueAsync(ev);

        await h.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.That(sendInvocations, Is.EqualTo(0),
            "SC-004: empty allowlist must result in zero provider calls.");

        var deliveries = await h.Db.NotificationDeliveries.ToListAsync();
        Assert.That(deliveries, Has.Count.EqualTo(1));
        Assert.That(deliveries[0].Status, Is.EqualTo(NotificationDeliveryStatus.BlockedByAllowlist));
        Assert.That(deliveries[0].LastError, Is.EqualTo("NotAllowlisted"));

        var outbox = await h.Db.NotificationOutbox.SingleAsync();
        Assert.That(outbox.Status, Is.EqualTo(NotificationOutboxStatus.Done),
            "FR-019: an allowlist drop is successful handling, not a failure.");
    }
}
