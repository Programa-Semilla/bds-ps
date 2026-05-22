// Spec 021 — see specs/021-feedback-session-may13/research.md R-2 + R-11.

namespace FundingPlatform.Domain.Interfaces;

/// <summary>
/// Spec 021 — narrow clock seam used by <c>StageExpiryReminderService</c> (and
/// the stage-window guard composed by <c>Application.Submit</c>) so integration
/// tests can advance time without touching <see cref="DateTimeOffset.UtcNow"/>.
///
/// Per R-11, integration tests inject a fake implementation that advances by
/// a configurable <c>TimeSpan</c>; production uses a system-clock implementation
/// in <c>FundingPlatform.Infrastructure.Clocks</c>.
/// </summary>
public interface IStageExpiryClock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
