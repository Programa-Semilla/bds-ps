// Spec 044 — see specs/044-process-reception-windows/contracts/interfaces.md
// (Application layer — IReceptionWindowQuery).

using FundingPlatform.Application.Processes.ReceptionWindows;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ReceptionWindows;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 044 / US1 — EF-backed <see cref="IReceptionWindowQuery"/>. The admin card
/// (<see cref="GetForProcessAsync"/>) returns all windows with a per-row computed
/// state; the gating/notice paths load only <b>active reception</b> windows and
/// run the pure <see cref="ReceptionWindowEvaluation.Evaluate"/>.
/// </summary>
public sealed class ReceptionWindowQuery : IReceptionWindowQuery
{
    private readonly AppDbContext _db;

    public ReceptionWindowQuery(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ReceptionWindowRow>> GetForProcessAsync(
        int processId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var windows = await _db.ProcessEvents.AsNoTracking()
            .Where(e => e.ProcessId == processId && e.EventType == ProcessEventType.ReceptionWindow)
            .OrderBy(e => e.DisplayOrder).ThenBy(e => e.StartUtc)
            .ToListAsync(ct);

        return windows
            .Select(e => new ReceptionWindowRow(
                e.Id, e.Name, e.StartUtc, e.EndUtc, e.ApplicantFacingMessage, e.Description,
                e.IsActive, e.DisplayOrder, e.ComputeState(nowUtc)))
            .ToList();
    }

    public async Task<ReceptionAvailability> GetAvailabilityForGroupAsync(
        int groupId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var processId = await _db.Groups
            .Where(g => g.Id == groupId)
            .Select(g => (int?)g.ProcessId)
            .FirstOrDefaultAsync(ct);

        return await EvaluateForProcessAsync(processId, nowUtc, ct);
    }

    public async Task<ReceptionAvailability> GetAvailabilityForApplicationAsync(
        int applicationId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        // Application → Group → Process.
        var processId = await (
            from a in _db.Applications
            where a.Id == applicationId
            join g in _db.Groups on a.GroupId equals g.Id
            select (int?)g.ProcessId).FirstOrDefaultAsync(ct);

        return await EvaluateForProcessAsync(processId, nowUtc, ct);
    }

    private async Task<ReceptionAvailability> EvaluateForProcessAsync(
        int? processId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (processId is null)
        {
            // No resolvable Process → no windows → unrestricted (FR-007).
            return ReceptionWindowEvaluation.Evaluate(Array.Empty<ReceptionWindowSnapshot>(), nowUtc);
        }

        var snapshots = await _db.ProcessEvents.AsNoTracking()
            // FR-002 — a reception window is the ReceptionWindow type WITH the
            // submission-control flag set; gating keys off the flag so future
            // event types (informational/deadline/milestone) never gate.
            .Where(e => e.ProcessId == processId
                && e.EventType == ProcessEventType.ReceptionWindow
                && e.ControlsSubmissionAvailability
                && e.IsActive)
            .Select(e => new ReceptionWindowSnapshot(
                e.Id, e.Name, e.StartUtc, e.EndUtc, e.ApplicantFacingMessage))
            .ToListAsync(ct);

        return ReceptionWindowEvaluation.Evaluate(snapshots, nowUtc);
    }
}
