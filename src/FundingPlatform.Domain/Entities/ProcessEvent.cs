// Spec 044 — see specs/044-process-reception-windows/data-model.md (ProcessEvent aggregate).

using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 044 / FR-001 — a configurable calendar item belonging to a
/// <see cref="Process"/>. The <see cref="ProcessEventType.ReceptionWindow"/> type
/// controls submission availability (<see cref="ControlsSubmissionAvailability"/>);
/// other types are reserved (schema-only, US5).
///
/// The entity validates its own invariants (<c>EndUtc &gt; StartUtc</c>, name
/// length) in the factory/<see cref="Update"/>; state transitions
/// (<see cref="Activate"/>/<see cref="Deactivate"/>) and the per-window state
/// computation (<see cref="ComputeState"/>) are entity behavior, not controller
/// logic. The cross-window submission gate is the pure
/// <see cref="ReceptionWindows.ReceptionWindowEvaluation"/>.
/// </summary>
public class ProcessEvent
{
    public const int MaxNameLength = 120;
    public const int MaxTextLength = 500;

    public int Id { get; private set; }
    public int ProcessId { get; private set; }
    public ProcessEventType EventType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset StartUtc { get; private set; }
    public DateTimeOffset EndUtc { get; private set; }
    public bool ControlsSubmissionAvailability { get; private set; }
    public string? ApplicantFacingMessage { get; private set; }
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string? CreatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedByUserId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public Process? Process { get; private set; }

    private ProcessEvent() { }

    /// <summary>
    /// Factory for a reception window. Throws <see cref="ArgumentException"/> if
    /// <paramref name="endUtc"/> ≤ <paramref name="startUtc"/> or the name is
    /// blank/too long. Sets <see cref="EventType"/> to
    /// <see cref="ProcessEventType.ReceptionWindow"/>,
    /// <see cref="ControlsSubmissionAvailability"/> to <c>true</c>, and
    /// <see cref="IsActive"/> to <c>true</c>.
    /// </summary>
    public static ProcessEvent CreateReceptionWindow(
        int processId,
        string name,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? applicantFacingMessage,
        string? description,
        int displayOrder,
        string? createdByUserId)
    {
        if (processId <= 0)
        {
            throw new ArgumentException("A reception window must belong to a Process.", nameof(processId));
        }

        return new ProcessEvent
        {
            ProcessId = processId,
            EventType = ProcessEventType.ReceptionWindow,
            ControlsSubmissionAvailability = true,
            Name = ValidateName(name),
            StartUtc = startUtc,
            EndUtc = ValidateRange(startUtc, endUtc),
            ApplicantFacingMessage = ValidateText(applicantFacingMessage, nameof(applicantFacingMessage)),
            Description = ValidateText(description, nameof(description)),
            DisplayOrder = displayOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId,
        };
    }

    /// <summary>Re-validates and applies edited fields. Re-checks <c>EndUtc &gt; StartUtc</c>.</summary>
    public void Update(
        string name,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? applicantFacingMessage,
        string? description,
        int displayOrder,
        string? updatedByUserId)
    {
        Name = ValidateName(name);
        StartUtc = startUtc;
        EndUtc = ValidateRange(startUtc, endUtc);
        ApplicantFacingMessage = ValidateText(applicantFacingMessage, nameof(applicantFacingMessage));
        Description = ValidateText(description, nameof(description));
        DisplayOrder = displayOrder;
        Stamp(updatedByUserId);
    }

    /// <summary>Marks the window active (no-op if already active).</summary>
    public void Activate(string? updatedByUserId)
    {
        if (IsActive)
        {
            return;
        }
        IsActive = true;
        Stamp(updatedByUserId);
    }

    /// <summary>Marks the window inactive — ignored by gating + display (no-op if already inactive).</summary>
    public void Deactivate(string? updatedByUserId)
    {
        if (!IsActive)
        {
            return;
        }
        IsActive = false;
        Stamp(updatedByUserId);
    }

    /// <summary>Pure point-in-time state for the admin badge.</summary>
    public ReceptionWindowState ComputeState(DateTimeOffset nowUtc)
    {
        if (nowUtc < StartUtc)
        {
            return ReceptionWindowState.Upcoming;
        }
        if (nowUtc < EndUtc)
        {
            return ReceptionWindowState.OpenNow;
        }
        return ReceptionWindowState.Closed;
    }

    private void Stamp(string? updatedByUserId)
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    private static string ValidateName(string name)
    {
        if (name is null)
        {
            throw new ArgumentException("El nombre de la ventana es obligatorio.", nameof(name));
        }
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("El nombre de la ventana es obligatorio.", nameof(name));
        }
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"El nombre debe tener {MaxNameLength} caracteres o menos.", nameof(name));
        }
        return trimmed;
    }

    private static DateTimeOffset ValidateRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException(
                "La fecha de cierre debe ser posterior a la fecha de apertura.", nameof(endUtc));
        }
        return endUtc;
    }

    private static string? ValidateText(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        if (trimmed.Length > MaxTextLength)
        {
            throw new ArgumentException(
                $"El texto debe tener {MaxTextLength} caracteres o menos.", paramName);
        }
        return trimmed;
    }
}
