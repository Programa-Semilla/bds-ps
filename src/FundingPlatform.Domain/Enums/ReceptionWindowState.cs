// Spec 044 — see specs/044-process-reception-windows/data-model.md.

namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 044 — the point-in-time state of a single reception window, used for the
/// admin per-row state badge (<see cref="FundingPlatform.Domain.Entities.ProcessEvent.ComputeState"/>).
/// Distinct from the process-wide <c>SubmissionAvailabilityStatus</c> the
/// applicant gate computes across all windows.
/// </summary>
public enum ReceptionWindowState
{
    /// <summary>now &lt; StartUtc.</summary>
    Upcoming,

    /// <summary>StartUtc ≤ now &lt; EndUtc.</summary>
    OpenNow,

    /// <summary>now ≥ EndUtc.</summary>
    Closed,
}
