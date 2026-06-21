using System.Globalization;

namespace FundingPlatform.Infrastructure.BackgroundServices;

/// <summary>
/// Spec 043 / research D4 (T041) — computes the next occurrence of a configured
/// wall-clock time-of-day in America/Costa_Rica. Shared by the daily Hacienda sync
/// and the stale-value digest workers so the next-run math (incl. day boundaries) is
/// DRY and unit-testable. Staleness math itself stays UTC-instant-based (FR-003);
/// only the *run time* is local.
/// </summary>
public static class DailyRunSchedule
{
    private static readonly TimeZoneInfo CostaRica = ResolveCostaRica();

    /// <summary>
    /// The next UTC instant at which <paramref name="runAtLocalTime"/> (e.g. "06:00")
    /// occurs in Costa Rica, strictly after <paramref name="nowUtc"/>.
    /// </summary>
    public static DateTime NextRunUtc(string runAtLocalTime, DateTime nowUtc)
    {
        var tod = ParseTimeOfDay(runAtLocalTime);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), CostaRica);

        var todayRunLocal = nowLocal.Date + tod;
        var nextLocal = nowLocal < todayRunLocal ? todayRunLocal : todayRunLocal.AddDays(1);

        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), CostaRica);
    }

    /// <summary>The delay from <paramref name="nowUtc"/> until the next run (never negative).</summary>
    public static TimeSpan TimeUntilNextRun(string runAtLocalTime, DateTime nowUtc)
    {
        var delay = NextRunUtc(runAtLocalTime, nowUtc) - nowUtc;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static TimeSpan ParseTimeOfDay(string runAtLocalTime)
        => TimeOnly.TryParse(runAtLocalTime, CultureInfo.InvariantCulture, out var t)
            ? t.ToTimeSpan()
            : new TimeSpan(6, 0, 0);

    private static TimeZoneInfo ResolveCostaRica()
    {
        foreach (var id in new[] { "America/Costa_Rica", "Central Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // CR has no DST → fixed UTC-6.
        return TimeZoneInfo.CreateCustomTimeZone("CR-UTC-6", TimeSpan.FromHours(-6), "Costa Rica", "Costa Rica");
    }
}
