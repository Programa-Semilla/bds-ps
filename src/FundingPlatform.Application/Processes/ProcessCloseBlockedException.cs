// Spec 021 — see specs/021-feedback-session-may13/contracts/admin-routes.md
// (POST /Admin/Processes/{id}/Close returns 422 with offending PublicCode list).

namespace FundingPlatform.Application.Processes;

/// <summary>
/// Spec 021 / OQ-2 — raised when an admin tries to close a Process that still
/// has Applications in an Active state (Borrador, Submitted, InReview, Signing).
/// Carries the offending Applications' <c>PublicCode</c>s so the controller can
/// surface them on the 422 response per contracts/admin-routes.md.
/// </summary>
public sealed class ProcessCloseBlockedException : Exception
{
    public int ProcessId { get; }
    public IReadOnlyList<string> ActivePublicCodes { get; }

    public ProcessCloseBlockedException(int processId, IReadOnlyList<string> activePublicCodes)
        : base($"Cannot close Process {processId}: {activePublicCodes.Count} active Application(s) attached.")
    {
        ProcessId = processId;
        ActivePublicCodes = activePublicCodes;
    }
}
