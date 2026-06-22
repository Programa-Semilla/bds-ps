// Spec 044 — see specs/044-process-reception-windows/contracts/interfaces.md
// (Application layer — IReceptionWindowQuery).

using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ReceptionWindows;

namespace FundingPlatform.Application.Processes.ReceptionWindows;

/// <summary>
/// Spec 044 — read-side for reception windows. The admin card lists all windows
/// (active + inactive) with a per-row computed state badge; the gating/notice
/// paths load the <b>active</b> reception windows for a Process and run the pure
/// <see cref="ReceptionWindowEvaluation.Evaluate"/>.
/// </summary>
public interface IReceptionWindowQuery
{
    /// <summary>Admin card: every window for a Process with computed state badge.</summary>
    Task<IReadOnlyList<ReceptionWindowRow>> GetForProcessAsync(
        int processId, DateTimeOffset nowUtc, CancellationToken ct);

    /// <summary>Gating/notice: availability for the Process owning a Group
    /// (Group → Process). Used by the new-draft creation guard + Create notice.</summary>
    Task<ReceptionAvailability> GetAvailabilityForGroupAsync(
        int groupId, DateTimeOffset nowUtc, CancellationToken ct);

    /// <summary>Gating/notice: availability for the Process owning an Application
    /// (Application → Group → Process). Used by the submit gate + Edit notice.</summary>
    Task<ReceptionAvailability> GetAvailabilityForApplicationAsync(
        int applicationId, DateTimeOffset nowUtc, CancellationToken ct);
}

/// <summary>Admin-card row: all fields + the per-row point-in-time state.</summary>
public sealed record ReceptionWindowRow(
    int Id,
    string Name,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? ApplicantFacingMessage,
    string? Description,
    bool IsActive,
    int DisplayOrder,
    ReceptionWindowState State);
