using FundingPlatform.Infrastructure.Time;
using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Tests.Unit.Infrastructure;

/// <summary>
/// Spec 044 / FR-005 / FR-010 / SC-007 — the CR-local↔UTC conversion that is the
/// sole home of the "−6h" business-timezone semantics (gating itself is pure UTC).
/// </summary>
[TestFixture]
public class BusinessTimeZoneTests
{
    private static BusinessTimeZone Build(string? zoneId)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(zoneId is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Process:BusinessTimeZone"] = zoneId })
            .Build();
        return new BusinessTimeZone(cfg);
    }

    [Test]
    public void ToUtc_TreatsInputAsCostaRicaLocal_AddsSixHours()
    {
        var tz = Build("America/Costa_Rica");

        // CR midnight 2026-03-01 → 06:00 UTC (CR is UTC−6, no DST).
        var utc = tz.ToUtc(new DateTime(2026, 3, 1, 0, 0, 0));

        Assert.That(utc.ToUniversalTime(), Is.EqualTo(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void ToBusinessLocal_ProjectsUtcIntoCostaRica_SubtractsSixHours()
    {
        var tz = Build("America/Costa_Rica");

        var local = tz.ToBusinessLocal(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero));

        Assert.That(local.Offset, Is.EqualTo(TimeSpan.FromHours(-6)));
        Assert.That(local.DateTime, Is.EqualTo(new DateTime(2026, 3, 1, 0, 0, 0)));
    }

    [Test]
    public void RoundTrip_PreservesWallClock()
    {
        var tz = Build("America/Costa_Rica");
        var input = new DateTime(2026, 6, 15, 14, 30, 0);

        var back = tz.ToBusinessLocal(tz.ToUtc(input));

        Assert.That(back.DateTime, Is.EqualTo(input));
    }

    [Test]
    public void UnknownZone_FallsBackToFixedMinusSixOffset()
    {
        // A bogus zone id must not throw; the fixed −06:00 fallback keeps conversions working.
        var tz = Build("Not/A_Real_Zone");

        var utc = tz.ToUtc(new DateTime(2026, 3, 1, 0, 0, 0));
        Assert.That(utc.ToUniversalTime(), Is.EqualTo(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero)));

        var local = tz.ToBusinessLocal(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero));
        Assert.That(local.Offset, Is.EqualTo(TimeSpan.FromHours(-6)));
    }

    [Test]
    public void MissingConfig_DefaultsToCostaRica()
    {
        var tz = Build(null);
        var utc = tz.ToUtc(new DateTime(2026, 3, 1, 0, 0, 0));
        Assert.That(utc.ToUniversalTime(), Is.EqualTo(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero)));
    }
}
