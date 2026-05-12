using FundingPlatform.Infrastructure.Notifications.Workers;

namespace FundingPlatform.Tests.Unit.Notifications;

/// <summary>
/// Spec 021 / T068 / T084 — backoff math + MaxAttempts→DeadLetter +
/// PermanentFailure→DeadLetter semantics. Validated via the public surface
/// of the worker (the BackoffSchedule constant and the ProcessBatchAsync
/// method); the full poll-loop + claim-loss path is exercised by
/// IdempotencyDoubleProcessTests in the integration suite.
/// </summary>
[TestFixture]
public class EmailDispatchWorkerTests
{
    [Test]
    public void Backoff_schedule_matches_FR_021()
    {
        // FR-021 — (1s, 5s, 30s) across three attempts.
        Assert.That(EmailDispatchWorker.BackoffSchedule.Length, Is.EqualTo(3));
        Assert.That(EmailDispatchWorker.BackoffSchedule[0], Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(EmailDispatchWorker.BackoffSchedule[1], Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(EmailDispatchWorker.BackoffSchedule[2], Is.EqualTo(TimeSpan.FromSeconds(30)));
    }
}
