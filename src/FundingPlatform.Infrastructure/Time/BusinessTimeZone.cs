// Spec 044 — see specs/044-process-reception-windows/research.md D1.

using FundingPlatform.Application.Time;
using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Infrastructure.Time;

/// <summary>
/// Spec 044 / D1 — config-driven <see cref="IBusinessTimeZone"/>. Resolves the
/// IANA/Windows zone id from <c>Process:BusinessTimeZone</c> (default
/// <c>America/Costa_Rica</c>). Costa Rica observes no DST, so the offset is a
/// constant −06:00; if the named zone is absent on the host the impl falls back
/// to a fixed −06:00 <see cref="TimeSpan"/> rather than throwing.
/// </summary>
public sealed class BusinessTimeZone : IBusinessTimeZone
{
    private const string DefaultZoneId = "America/Costa_Rica";
    private static readonly TimeSpan FallbackOffset = TimeSpan.FromHours(-6);

    private readonly TimeZoneInfo? _zone;
    private readonly TimeSpan _fixedOffset;

    public BusinessTimeZone(IConfiguration configuration)
    {
        var zoneId = configuration["Process:BusinessTimeZone"];
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            zoneId = DefaultZoneId;
        }

        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            _fixedOffset = _zone.BaseUtcOffset;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // CR has no DST; a fixed −06:00 offset is a safe, drift-free fallback.
            _zone = null;
            _fixedOffset = FallbackOffset;
        }
    }

    public DateTimeOffset ToUtc(DateTime businessLocal)
    {
        // The datetime-local input has no zone; treat it as a CR wall-clock value.
        var local = DateTime.SpecifyKind(businessLocal, DateTimeKind.Unspecified);
        var offset = _zone?.GetUtcOffset(local) ?? _fixedOffset;
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public DateTimeOffset ToBusinessLocal(DateTimeOffset utc)
        => _zone is not null
            ? TimeZoneInfo.ConvertTime(utc, _zone)
            : utc.ToOffset(_fixedOffset);
}
