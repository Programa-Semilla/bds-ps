// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Process aggregate)
// and research.md OQ-2 / OQ-3.

using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 021 / FR-001 — top-level lifecycle aggregate above <see cref="Group"/>.
/// Each annual cycle (e.g. *Crocus 2025*) is one Process; every Group belongs
/// to exactly one Process. Closing a Process freezes all attached
/// FundingAgreements (OQ-2) and blocks further Application submissions.
///
/// Stage-window overrides are nullable per-Process columns (FR-006 / OQ-3) —
/// null means "use the platform default from SystemConfiguration".
/// </summary>
public class Process
{
    public const int MaxNameLength = 120;

    private readonly List<Group> _groups = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ProcessStatus Status { get; private set; }

    public int? SolicitudWindowDays { get; private set; }
    public int? RevisionWindowDays { get; private set; }
    public int? FacturacionWindowDays { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public ProcessPlantilla? Plantilla { get; internal set; }
    public IReadOnlyCollection<Group> Groups => _groups.AsReadOnly();

    private Process() { }

    private Process(string name, DateTimeOffset now)
    {
        Name = name;
        Status = ProcessStatus.Active;
        CreatedAt = now;
    }

    /// <summary>
    /// Factory: a new Process is always created Active. Catalog uniqueness within
    /// the active set is enforced at the application layer (reuse across closed
    /// cycles is allowed per data-model.md).
    /// </summary>
    public static Process Create(string name)
        => new(ValidateName(name), DateTimeOffset.UtcNow);

    /// <summary>
    /// Renames the Process. Trimmed value persisted.
    /// </summary>
    public void Rename(string newName)
    {
        var trimmed = ValidateName(newName);
        if (string.Equals(trimmed, Name, StringComparison.Ordinal))
        {
            return;
        }
        Name = trimmed;
    }

    /// <summary>
    /// Transitions <see cref="Status"/> from Active to Closed and stamps
    /// <see cref="ClosedAt"/>. Throws <see cref="ProcessClosedException"/> if the
    /// Process is already closed. The caller (Application layer command) is
    /// responsible for asserting there are no Active Applications attached via
    /// Groups (OQ-2) and for writing the <c>ProcessClosed</c> audit event.
    /// </summary>
    public void Close()
    {
        if (Status == ProcessStatus.Closed)
        {
            throw new ProcessClosedException(Id, ClosedAt);
        }
        Status = ProcessStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sets or clears the per-Process override for the given stage. Passing
    /// <c>null</c> reverts to the platform default from <c>SystemConfiguration</c>.
    /// Positive integer required when non-null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">days ≤ 0.</exception>
    /// <exception cref="ProcessClosedException">Process is closed.</exception>
    public void OverrideStageWindow(StageKind stage, int? days)
    {
        if (Status == ProcessStatus.Closed)
        {
            throw new ProcessClosedException(Id, ClosedAt);
        }
        if (days is not null and <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(days), days, "Stage window days must be positive (or null to revert to default).");
        }

        switch (stage)
        {
            case StageKind.Solicitud:
                SolicitudWindowDays = days;
                break;
            case StageKind.Revision:
                RevisionWindowDays = days;
                break;
            case StageKind.Facturacion:
                FacturacionWindowDays = days;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown stage.");
        }
    }

    /// <summary>
    /// Returns the per-Process override for <paramref name="stage"/>, or
    /// <c>null</c> if no override is set (caller falls back to the platform
    /// default in <c>SystemConfiguration</c>).
    /// </summary>
    public int? OverrideForStage(StageKind stage) => stage switch
    {
        StageKind.Solicitud => SolicitudWindowDays,
        StageKind.Revision => RevisionWindowDays,
        StageKind.Facturacion => FacturacionWindowDays,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown stage."),
    };

    private static string ValidateName(string name)
    {
        if (name is null)
        {
            throw new ArgumentException("Process name is required.", nameof(name));
        }
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Process name is required.", nameof(name));
        }
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Process name must be {MaxNameLength} characters or fewer.", nameof(name));
        }
        return trimmed;
    }
}
