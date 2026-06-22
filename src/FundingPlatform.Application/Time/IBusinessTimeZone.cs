// Spec 044 — see specs/044-process-reception-windows/research.md D1
// and contracts/interfaces.md (IBusinessTimeZone).

namespace FundingPlatform.Application.Time;

/// <summary>
/// Spec 044 / D1 — the business-operating timezone (Costa Rica) used **only at
/// the Web boundary** for reception-window admin input and applicant display.
///
/// Gating itself never calls this: both the windows and "now" are absolute UTC
/// instants, so the open/closed/upcoming determination is a pure UTC comparison
/// (see <see cref="FundingPlatform.Domain.ReceptionWindows.ReceptionWindowEvaluation"/>).
/// This abstraction converts a CR-local <c>datetime-local</c> input to UTC at
/// save time, and a stored UTC instant back to CR-local for display.
/// </summary>
public interface IBusinessTimeZone
{
    /// <summary>Interprets an admin <c>datetime-local</c> value as a Costa Rica
    /// local wall-clock instant and returns its absolute UTC equivalent.</summary>
    DateTimeOffset ToUtc(DateTime businessLocal);

    /// <summary>Projects an absolute UTC instant into Costa Rica local time for
    /// es-CR display (<c>dd/MM/yyyy HH:mm</c>).</summary>
    DateTimeOffset ToBusinessLocal(DateTimeOffset utc);

    /// <summary>The current UTC offset of the business timezone (−06:00 for CR,
    /// which observes no DST).</summary>
    TimeSpan CurrentOffset { get; }
}
