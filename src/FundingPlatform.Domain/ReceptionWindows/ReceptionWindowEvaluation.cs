// Spec 044 — see specs/044-process-reception-windows/data-model.md
// (pure evaluation value objects + Evaluate).

namespace FundingPlatform.Domain.ReceptionWindows;

/// <summary>
/// Spec 044 — process-wide submission availability computed across all active
/// reception windows for a Process.
/// </summary>
public enum SubmissionAvailabilityStatus
{
    /// <summary>No reception windows configured — submission unrestricted (FR-007).</summary>
    Unrestricted,

    /// <summary>now is inside at least one active window — submission allowed.</summary>
    Open,

    /// <summary>now is before the first (earliest) window — none has opened yet.</summary>
    BeforeFirstWindow,

    /// <summary>now is between two windows (at least one closed, a later one upcoming).</summary>
    BetweenWindows,

    /// <summary>Every configured window has closed — submission permanently refused.</summary>
    AllWindowsClosed,
}

/// <summary>
/// Spec 044 — a minimal projection of an <b>active</b> reception window passed to
/// <see cref="ReceptionWindowEvaluation.Evaluate"/>. Decouples the pure gate from EF.
/// </summary>
public sealed record ReceptionWindowSnapshot(
    int Id,
    string Name,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? ApplicantFacingMessage);

/// <summary>
/// Spec 044 — the result of evaluating submission availability at an instant.
/// </summary>
public sealed record ReceptionAvailability(
    SubmissionAvailabilityStatus Status,
    ReceptionWindowSnapshot? ActiveWindow,
    ReceptionWindowSnapshot? NextWindow,
    ReceptionWindowSnapshot? LastClosedWindow)
{
    /// <summary>Submission is allowed when unrestricted or inside an open window.</summary>
    public bool CanSubmit => Status is SubmissionAvailabilityStatus.Unrestricted or SubmissionAvailabilityStatus.Open;

    /// <summary>FR-014 — a new draft may be started unless every window has closed
    /// (a future window still gives a submission chance).</summary>
    public bool CanCreateDraft => Status != SubmissionAvailabilityStatus.AllWindowsClosed;
}

/// <summary>
/// Spec 044 / D3 — pure gate over absolute UTC instants. No timezone math: both
/// the windows and "now" are UTC, so the open/closed/upcoming determination is a
/// pure comparison and cannot drift across the CR/UTC offset.
/// </summary>
public static class ReceptionWindowEvaluation
{
    /// <summary>
    /// Evaluates submission availability across the supplied active windows at
    /// <paramref name="nowUtc"/>:
    /// <list type="bullet">
    ///   <item>empty → <see cref="SubmissionAvailabilityStatus.Unrestricted"/></item>
    ///   <item>any window with <c>Start ≤ now &lt; End</c> → <see cref="SubmissionAvailabilityStatus.Open"/>
    ///   (if several overlap, the one with the latest <c>End</c> drives the close countdown)</item>
    ///   <item>else the earliest window with <c>Start &gt; now</c> →
    ///   <see cref="SubmissionAvailabilityStatus.BeforeFirstWindow"/> when no window has closed yet,
    ///   otherwise <see cref="SubmissionAvailabilityStatus.BetweenWindows"/></item>
    ///   <item>else (all windows closed) → <see cref="SubmissionAvailabilityStatus.AllWindowsClosed"/></item>
    /// </list>
    /// </summary>
    public static ReceptionAvailability Evaluate(
        IReadOnlyList<ReceptionWindowSnapshot> windows, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (windows.Count == 0)
        {
            return new ReceptionAvailability(SubmissionAvailabilityStatus.Unrestricted, null, null, null);
        }

        // Open: now ∈ [Start, End). On overlap, prefer the latest End so the
        // applicant countdown reflects the furthest close instant.
        ReceptionWindowSnapshot? open = null;
        foreach (var w in windows)
        {
            if (w.StartUtc <= nowUtc && nowUtc < w.EndUtc
                && (open is null || w.EndUtc > open.EndUtc))
            {
                open = w;
            }
        }
        if (open is not null)
        {
            return new ReceptionAvailability(SubmissionAvailabilityStatus.Open, open, null, null);
        }

        // Next upcoming window (earliest Start strictly after now).
        ReceptionWindowSnapshot? next = null;
        foreach (var w in windows)
        {
            if (w.StartUtc > nowUtc && (next is null || w.StartUtc < next.StartUtc))
            {
                next = w;
            }
        }

        var anyClosed = windows.Any(w => w.EndUtc <= nowUtc);

        if (next is not null)
        {
            var status = anyClosed
                ? SubmissionAvailabilityStatus.BetweenWindows
                : SubmissionAvailabilityStatus.BeforeFirstWindow;
            return new ReceptionAvailability(status, null, next, null);
        }

        // No open and no upcoming → everything has closed.
        ReceptionWindowSnapshot? lastClosed = null;
        foreach (var w in windows)
        {
            if (lastClosed is null || w.EndUtc > lastClosed.EndUtc)
            {
                lastClosed = w;
            }
        }
        return new ReceptionAvailability(SubmissionAvailabilityStatus.AllWindowsClosed, null, null, lastClosed);
    }
}
