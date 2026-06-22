// Spec 044 / US3 — see specs/044-process-reception-windows/contracts/interfaces.md.

using FundingPlatform.Application.Time;
using FundingPlatform.Domain.ReceptionWindows;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 044 / US3 — pure-render ViewModel for <c>_ReceptionWindowNotice</c>. The
/// controller computes the boundary instant (already projected to Costa Rica local
/// time) and the remaining time server-side; the partial only formats them.
/// <see cref="SubmissionAvailabilityStatus.Unrestricted"/> renders nothing.
/// </summary>
public sealed class ReceptionWindowNoticeViewModel
{
    public SubmissionAvailabilityStatus Status { get; init; }

    /// <summary>CR-local instant: the close instant when Open, the next-open instant
    /// when Upcoming/Between, the last-closed instant when AllWindowsClosed.</summary>
    public DateTime? BoundaryLocal { get; init; }

    /// <summary>Time remaining until close (server-computed) when Open; null otherwise.</summary>
    public TimeSpan? Remaining { get; init; }

    /// <summary>Optional admin-authored message attached to the relevant window.</summary>
    public string? ApplicantMessage { get; init; }

    /// <summary>True unless unrestricted (no windows configured).</summary>
    public bool ShouldRender => Status != SubmissionAvailabilityStatus.Unrestricted;

    public bool CanSubmit => Status is SubmissionAvailabilityStatus.Unrestricted
        or SubmissionAvailabilityStatus.Open;

    /// <summary>Projects a domain <see cref="ReceptionAvailability"/> into the notice VM:
    /// picks the relevant boundary window, converts the instant to CR local, and
    /// server-computes the remaining time when open.</summary>
    public static ReceptionWindowNoticeViewModel FromAvailability(
        ReceptionAvailability availability, IBusinessTimeZone tz, DateTimeOffset nowUtc)
    {
        var (boundaryUtc, message) = availability.Status switch
        {
            SubmissionAvailabilityStatus.Open =>
                ((DateTimeOffset?)availability.ActiveWindow!.EndUtc, availability.ActiveWindow!.ApplicantFacingMessage),
            SubmissionAvailabilityStatus.BeforeFirstWindow or SubmissionAvailabilityStatus.BetweenWindows =>
                (availability.NextWindow!.StartUtc, availability.NextWindow!.ApplicantFacingMessage),
            SubmissionAvailabilityStatus.AllWindowsClosed =>
                (availability.LastClosedWindow!.EndUtc, (string?)null),
            _ => ((DateTimeOffset?)null, (string?)null),
        };

        return new ReceptionWindowNoticeViewModel
        {
            Status = availability.Status,
            BoundaryLocal = boundaryUtc is { } b ? tz.ToBusinessLocal(b).DateTime : null,
            Remaining = availability.Status == SubmissionAvailabilityStatus.Open
                ? availability.ActiveWindow!.EndUtc - nowUtc
                : null,
            ApplicantMessage = message,
        };
    }
}
