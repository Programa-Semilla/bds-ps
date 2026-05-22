// Spec 021 — see specs/021-feedback-session-may13/research.md R-2 + R-11.

using FundingPlatform.Domain.Interfaces;

namespace FundingPlatform.Infrastructure.Clocks;

/// <summary>
/// Spec 021 / T116 (early-landed by US2 / T091) — production
/// <see cref="IStageExpiryClock"/> implementation backed by
/// <see cref="DateTimeOffset.UtcNow"/>.
///
/// <para>Integration tests replace this with a fake that advances by a
/// configurable <c>TimeSpan</c> (R-11) so reminder-cadence + stage-window
/// boundary scenarios can be deterministically simulated without touching
/// the system clock.</para>
/// </summary>
public sealed class SystemStageExpiryClock : IStageExpiryClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
