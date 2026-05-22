using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 021 / T055 / FR-024 — model for the stage-countdown banner partial
/// rendered on applicant draft, reviewer queue rows, and signing inbox rows.
///
/// The partial is "dumb" — it only renders. All time math (remaining,
/// danger threshold) happens against <see cref="Now"/> so the caller can
/// freeze time for tests (deterministic E2E renders).
/// </summary>
public sealed class StageCountdownBannerViewModel
{
    public StageKind StageKind { get; init; }
    public DateTimeOffset EnteredAt { get; init; }
    public DateTimeOffset ClosesAt { get; init; }
    public DateTimeOffset Now { get; init; }
    public bool Closed { get; init; }

    /// <summary>
    /// Positive when the stage is still open; zero or negative when closed.
    /// </summary>
    public TimeSpan Remaining => ClosesAt - Now;

    /// <summary>
    /// FR-024 — danger styling kicks in once the remaining window drops
    /// below 24 hours.
    /// </summary>
    public bool IsDanger => !Closed && Remaining > TimeSpan.Zero && Remaining < TimeSpan.FromHours(24);
}
