// Spec 044 — see specs/044-process-reception-windows/contracts/interfaces.md
// (Application layer — IReceptionWindowService).

namespace FundingPlatform.Application.Processes.ReceptionWindows;

/// <summary>
/// Spec 044 / US1 — admin CRUD for a Process's reception windows. Mirrors
/// <c>IProcessService</c>/<c>IFundService</c>: every mutation writes an
/// <c>AdminAuditEvent</c> (<c>process.reception_window.*</c>) in the same
/// UnitOfWork. Validation (<c>EndUtc &gt; StartUtc</c>, name) is enforced by the
/// <see cref="FundingPlatform.Domain.Entities.ProcessEvent"/> factory/<c>Update</c>;
/// the service surfaces <see cref="ArgumentException"/> for the controller to map
/// to an es-CR <c>ModelState</c> error.
/// </summary>
public interface IReceptionWindowService
{
    /// <summary>Creates a reception window. Returns the new window id.</summary>
    Task<int> CreateAsync(CreateReceptionWindowCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>Edits an existing reception window's fields.</summary>
    Task UpdateAsync(UpdateReceptionWindowCommand cmd, string actorUserId, CancellationToken ct);

    /// <summary>Activates/deactivates a window (inactive windows are ignored by gating + display).</summary>
    Task SetActiveAsync(int windowId, bool isActive, string actorUserId, CancellationToken ct);

    /// <summary>Deletes a reception window.</summary>
    Task DeleteAsync(int windowId, string actorUserId, CancellationToken ct);
}

/// <summary>StartUtc/EndUtc are absolute UTC instants (the controller converts the
/// admin's CR-local <c>datetime-local</c> input via <c>IBusinessTimeZone.ToUtc</c>).</summary>
public sealed record CreateReceptionWindowCommand(
    int ProcessId,
    string Name,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? ApplicantFacingMessage,
    string? Description,
    int DisplayOrder);

public sealed record UpdateReceptionWindowCommand(
    int WindowId,
    string Name,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? ApplicantFacingMessage,
    string? Description,
    int DisplayOrder);
