// Spec 021 — see specs/021-feedback-session-may13/research.md R-2 + R-11
// and data-model.md (Process stage windows + Application.RemindersSentMask).

using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Application.Abstractions;

/// <summary>
/// Spec 021 / FR-006 / R-2 — reminder bucket emitted by
/// <see cref="IStageExpiryEvaluator.DetermineBucket"/>. Each Application's
/// <c>RemindersSentMask</c> bit-field tracks which buckets have already
/// fired (0x1 = T-72h, 0x2 = T-24h, 0x4 = expiry), guaranteeing at-most-once
/// delivery per stage entry.
/// </summary>
public enum ReminderBucket
{
    /// <summary>Stage window is open and outside the T-72h alarm horizon.</summary>
    None = 0,
    /// <summary>Within 72h of stage close — fire the T-72h reminder if 0x1 unset.</summary>
    T72h = 1,
    /// <summary>Within 24h of stage close — fire the T-24h reminder if 0x2 unset.</summary>
    T24h = 2,
    /// <summary>Stage window is closed — fire the expiry notice if 0x4 unset.</summary>
    Expired = 3,
}

/// <summary>
/// Spec 021 / FR-006 — composes the per-Application stage-window timestamp
/// from <c>Process</c> override → <c>SystemConfiguration</c> default, and
/// classifies the current instant into a <see cref="ReminderBucket"/>.
///
/// Pure function: no DB writes, no side effects. The hosted reminder service
/// calls <see cref="EvaluateFor"/> + <see cref="DetermineBucket"/> per cycle
/// and uses the <c>RemindersSentMask</c> to short-circuit already-fired
/// buckets.
/// </summary>
public interface IStageExpiryEvaluator
{
    /// <summary>
    /// Returns the current stage, the instant the Application entered it, and
    /// the absolute close-at instant computed by adding the resolved window
    /// (per-Process override fallthrough to SystemConfiguration default) to
    /// <c>StageEnteredAt</c>.
    /// </summary>
    Task<(StageKind CurrentStage, DateTimeOffset EnteredAt, DateTimeOffset ClosesAt)> EvaluateForAsync(
        AppEntity application, CancellationToken ct = default);

    /// <summary>
    /// Classifies <paramref name="now"/> against <paramref name="closesAt"/>
    /// using the supplied <paramref name="sentMask"/> to avoid double-firing.
    /// Returns <see cref="ReminderBucket.None"/> when the appropriate bit is
    /// already set OR the instant has not crossed the next bucket boundary.
    /// </summary>
    ReminderBucket DetermineBucket(DateTimeOffset closesAt, byte sentMask, DateTimeOffset now);
}
