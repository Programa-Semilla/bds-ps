using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

public class ComparisonJobBehaviorTests
{
    private static ComparisonJob NewPending() => ComparisonJob.Enqueue(
        applicationItemId: 1,
        requestedByUserId: "user-1",
        bypassedRateLimit: false,
        bypassedTokenCap: false,
        now: DateTimeOffset.UtcNow);

    [Test]
    public void Enqueue_StartsPending_AndStampsLastStatusChangeAt()
    {
        var now = DateTimeOffset.UtcNow;
        var job = ComparisonJob.Enqueue(5, "u", false, false, now);

        Assert.That(job.Status, Is.EqualTo(ComparisonJobStatus.Pending));
        Assert.That(job.LastStatusChangeAt, Is.EqualTo(now));
        Assert.That(job.StartedAt, Is.Null);
        Assert.That(job.FinishedAt, Is.Null);
    }

    [Test]
    public void Start_TransitionsPendingToRunning_AndStampsStartedAt()
    {
        var job = NewPending();
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        job.Start(now);
        Assert.That(job.Status, Is.EqualTo(ComparisonJobStatus.Running));
        Assert.That(job.StartedAt, Is.EqualTo(now));
        Assert.That(job.LastStatusChangeAt, Is.EqualTo(now));
    }

    [Test]
    public void Start_FromRunning_Throws()
    {
        var job = NewPending();
        job.Start(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => job.Start(DateTimeOffset.UtcNow));
    }

    [Test]
    public void RecordSuccess_FromRunning_Transitions()
    {
        var job = NewPending();
        job.Start(DateTimeOffset.UtcNow);
        job.RecordSuccess(123, DateTimeOffset.UtcNow);
        Assert.That(job.Status, Is.EqualTo(ComparisonJobStatus.Completed));
        Assert.That(job.ResultingArtifactId, Is.EqualTo(123));
        Assert.That(job.FinishedAt, Is.Not.Null);
    }

    [Test]
    public void RecordSuccess_FromPending_Throws()
    {
        var job = NewPending();
        Assert.Throws<InvalidOperationException>(() =>
            job.RecordSuccess(1, DateTimeOffset.UtcNow));
    }

    [Test]
    public void RecordFailure_FromPending_AndFromRunning_BothTransition()
    {
        var preFlight = NewPending();
        preFlight.RecordFailure("rate_limit_exceeded", DateTimeOffset.UtcNow);
        Assert.That(preFlight.Status, Is.EqualTo(ComparisonJobStatus.Failed));
        Assert.That(preFlight.FailureReason, Is.EqualTo("rate_limit_exceeded"));

        var midRun = NewPending();
        midRun.Start(DateTimeOffset.UtcNow);
        midRun.RecordFailure("provider_transient", DateTimeOffset.UtcNow);
        Assert.That(midRun.Status, Is.EqualTo(ComparisonJobStatus.Failed));
    }

    [Test]
    public void RecordFailure_FromCompleted_Throws()
    {
        var job = NewPending();
        job.Start(DateTimeOffset.UtcNow);
        job.RecordSuccess(1, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            job.RecordFailure("x", DateTimeOffset.UtcNow));
    }

    [Test]
    public void Reap_RunningOlderThanCutoff_TransitionsToFailedWorkerCrashed()
    {
        var job = NewPending();
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        job.Start(started);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);

        var reaped = job.Reap(cutoff, DateTimeOffset.UtcNow);

        Assert.That(reaped, Is.True);
        Assert.That(job.Status, Is.EqualTo(ComparisonJobStatus.Failed));
        Assert.That(job.FailureReason, Is.EqualTo("worker_crashed"));
    }

    [Test]
    public void Reap_RunningFreshEnough_NoOp()
    {
        var job = NewPending();
        job.Start(DateTimeOffset.UtcNow);
        var reaped = job.Reap(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);
        Assert.That(reaped, Is.False);
        Assert.That(job.Status, Is.EqualTo(ComparisonJobStatus.Running));
    }

    [Test]
    public void Reap_PendingJob_NoOp()
    {
        var job = NewPending();
        var reaped = job.Reap(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
        Assert.That(reaped, Is.False);
    }
}
