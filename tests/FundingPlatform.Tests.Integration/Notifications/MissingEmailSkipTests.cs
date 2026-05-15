using FundingPlatform.Domain.Notifications;
using FundingPlatform.Infrastructure.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 021 / T080 / FR-029 — recipient with null/empty email is recorded as
/// Skipped with LastError="MissingEmail"; the worker does NOT contact the provider.
/// </summary>
[TestFixture]
public class MissingEmailSkipTests
{
    [Test]
    public async Task Applicant_with_null_email_yields_Skipped_delivery()
    {
        var sendCount = 0;
        await using var h = await NotificationsTestHarness.CreateAsync(
            simulateMissingEmail: true,
            onSend: _ => Interlocked.Increment(ref sendCount));

        await h.SeedApplicationAsync();
        await h.EnqueueAsync(NotificationEvent.ApplicationSubmittedApplicant);

        await h.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.That(sendCount, Is.EqualTo(0),
            "FR-029: missing email must not produce a provider call.");

        var deliveries = await h.Db.NotificationDeliveries.ToListAsync();
        Assert.That(deliveries, Has.Count.EqualTo(1));
        Assert.That(deliveries[0].Status, Is.EqualTo(NotificationDeliveryStatus.Skipped));
        Assert.That(deliveries[0].LastError, Is.EqualTo("MissingEmail"));

        var outbox = await h.Db.NotificationOutbox.SingleAsync();
        Assert.That(outbox.Status, Is.EqualTo(NotificationOutboxStatus.Done),
            "Outbox row is Done even when every recipient was skipped — the worker handled it.");
    }
}
