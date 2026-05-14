// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Application.Submit guard).

using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Exceptions;

/// <summary>
/// Spec 021 / FR-006 / FR-024 — raised by the domain when a stage-bound mutation
/// (most commonly <see cref="FundingPlatform.Domain.Entities.Application.Submit(int)"/>)
/// is attempted after the active stage window has closed.
///
/// The Web layer maps this to HTTP 422 via a global exception filter (R-13);
/// the user-facing copy is the formatted Spanish message exposed by <see cref="Message"/>.
/// </summary>
public sealed class StageWindowClosedException : Exception
{
    public string ErrorCode { get; } = "STAGE_WINDOW_CLOSED";

    public StageKind Stage { get; }
    public DateTimeOffset ClosedAt { get; }

    public StageWindowClosedException(StageKind stage, DateTimeOffset closedAt)
        : base(BuildMessage(closedAt))
    {
        Stage = stage;
        ClosedAt = closedAt;
    }

    private static string BuildMessage(DateTimeOffset closedAt)
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "La etapa cerró el {0}. Contacte al administrador.",
            closedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
}
