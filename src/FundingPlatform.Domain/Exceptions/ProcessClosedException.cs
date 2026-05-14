// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Process.Close).

namespace FundingPlatform.Domain.Exceptions;

/// <summary>
/// Spec 021 / OQ-2 — raised when a caller attempts to close an already-closed
/// <see cref="FundingPlatform.Domain.Entities.Process"/>, or when a write path
/// targets a frozen Process (no further Applications, no agreement mutation).
///
/// The Web layer maps this to HTTP 422 via the same global exception filter
/// used for <see cref="StageWindowClosedException"/>.
/// </summary>
public sealed class ProcessClosedException : Exception
{
    public string ErrorCode { get; } = "PROCESS_CLOSED";

    public int ProcessId { get; }
    public DateTimeOffset? ClosedAt { get; }

    public ProcessClosedException(int processId, DateTimeOffset? closedAt = null)
        : base(BuildMessage(processId, closedAt))
    {
        ProcessId = processId;
        ClosedAt = closedAt;
    }

    private static string BuildMessage(int processId, DateTimeOffset? closedAt)
    {
        if (closedAt is null)
        {
            return $"El proceso {processId} está cerrado. Contacte al administrador.";
        }
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "El proceso {0} cerró el {1}. Contacte al administrador.",
            processId,
            closedAt.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }
}
