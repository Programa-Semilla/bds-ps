using FundingPlatform.Infrastructure.BackgroundServices;

namespace FundingPlatform.Tests.Unit.Infrastructure;

/// <summary>
/// Spec 043 / research D4 (T041) — the shared next-daily-run scheduler used by both daily
/// workers. Costa Rica is UTC-6 (no DST), so 06:00 CR == 12:00 UTC.
/// </summary>
[TestFixture]
public class DailyRunScheduleTests
{
    [Test]
    public void BeforeRunTimeToday_NextRunIsTodayAtLocalTime()
    {
        // 10:00 UTC == 04:00 CR, before 06:00 → today 06:00 CR == 12:00 UTC.
        var now = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
        var next = DailyRunSchedule.NextRunUtc("06:00", now);
        Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void AfterRunTimeToday_NextRunIsTomorrow()
    {
        // 13:00 UTC == 07:00 CR, after 06:00 → tomorrow 06:00 CR == next-day 12:00 UTC.
        var now = new DateTime(2026, 6, 21, 13, 0, 0, DateTimeKind.Utc);
        var next = DailyRunSchedule.NextRunUtc("06:00", now);
        Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ExactlyAtRunTime_RollsToNextDay()
    {
        // 12:00 UTC == 06:00 CR exactly → not strictly before, so next is tomorrow.
        var now = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var next = DailyRunSchedule.NextRunUtc("06:00", now);
        Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void TimeUntilNextRun_IsPositiveAndMatchesNextRun()
    {
        var now = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
        var delay = DailyRunSchedule.TimeUntilNextRun("06:00", now);
        Assert.That(delay, Is.EqualTo(TimeSpan.FromHours(2)));
    }

    [Test]
    public void UnparseableTime_FallsBackToSixAm()
    {
        var now = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
        var next = DailyRunSchedule.NextRunUtc("not-a-time", now);
        Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc)));
    }
}
