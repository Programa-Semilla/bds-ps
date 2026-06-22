// Spec 044 — see specs/044-process-reception-windows/research.md D3
// and contracts/interfaces.md (Domain layer).

using FundingPlatform.Domain.ReceptionWindows;

namespace FundingPlatform.Domain.Exceptions;

/// <summary>
/// Spec 044 / FR-008 — thrown by the submission gate when the current instant is
/// not inside an active reception window. Carries the
/// <see cref="SubmissionAvailabilityStatus"/> and the relevant boundary instant
/// (next-open when upcoming/between, last-closed when all windows are closed) so
/// the Web layer can compose the typed es-CR refusal (mapped to HTTP 422 by
/// <c>DomainExceptionFilter</c>). Replaces the spec-021 Solicitud-duration gate.
/// </summary>
public sealed class ReceptionWindowClosedException : Exception
{
    public SubmissionAvailabilityStatus Status { get; }

    /// <summary>The instant that explains the refusal: next-open
    /// (<see cref="SubmissionAvailabilityStatus.BeforeFirstWindow"/>/<see cref="SubmissionAvailabilityStatus.BetweenWindows"/>)
    /// or last-closed (<see cref="SubmissionAvailabilityStatus.AllWindowsClosed"/>). Null when not applicable.</summary>
    public DateTimeOffset? BoundaryUtc { get; }

    public ReceptionWindowClosedException(SubmissionAvailabilityStatus status, DateTimeOffset? boundaryUtc)
        : base($"Submission refused: reception window status is {status}.")
    {
        Status = status;
        BoundaryUtc = boundaryUtc;
    }
}
