using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 / T076 / SC-004 / FR-017 / FR-018 — non-prod allowlist with no
/// entries blocks every recipient. Zero provider calls; one BlockedByAllowlist
/// delivery row per intended recipient.
/// </summary>
[TestFixture]
public class AllowlistFailClosedTests
{
    [Test]
    public async Task Empty_allowlist_blocks_every_recipient_zero_provider_calls()
    {
        var sendInvocations = 0;
        await using var h = await NotificationsTestHarness.CreateAsync(
            senderOutcome: EmailSendOutcome.Sent,
            allowlistEntry: string.Empty,   // single empty entry → fails the match logic
            onSend: _ => Interlocked.Increment(ref sendInvocations));

        await h.SeedApplicationAsync();
        await h.EnqueueAsync(NotificationEvent.ApplicationSubmittedApplicant);

        await h.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.That(sendInvocations, Is.EqualTo(0),
            "SC-004 / FR-018: empty allowlist must result in zero provider calls.");

        var deliveries = await h.Db.NotificationDeliveries.ToListAsync();
        Assert.That(deliveries, Has.Count.EqualTo(1),
            "Each intended recipient must yield a delivery row.");
        Assert.That(deliveries[0].Status,
            Is.EqualTo(NotificationDeliveryStatus.BlockedByAllowlist));
        Assert.That(deliveries[0].LastError, Is.EqualTo("NotAllowlisted"));

        var outbox = await h.Db.NotificationOutbox.SingleAsync();
        // Allowlist drop is NOT a failure for the outbox row — it's a "successful"
        // handling per FR-019 (the worker did what it was supposed to do).
        Assert.That(outbox.Status, Is.EqualTo(NotificationOutboxStatus.Done));
    }
}
