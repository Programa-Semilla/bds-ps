// Spec 044 — see specs/044-process-reception-windows/research.md D7
// and contracts/interfaces.md (Application layer — IReceptionWindowService).

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Processes.ReceptionWindows;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 044 / US1 — implements <see cref="IReceptionWindowService"/>. Every
/// mutation stages an <c>AdminAuditEvent</c> (<c>process.reception_window.*</c>)
/// via <see cref="IAdminAuditEventWriter"/> and commits in the same UnitOfWork.
/// Mirrors <c>ProcessService</c>/<c>FundService</c> for transactional discipline.
/// Domain validation (<c>EndUtc &gt; StartUtc</c>, name) surfaces as
/// <see cref="ArgumentException"/> for the controller to map to es-CR.
/// </summary>
public sealed class ReceptionWindowService : IReceptionWindowService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;

    public ReceptionWindowService(AppDbContext db, IAdminAuditEventWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<int> CreateAsync(
        CreateReceptionWindowCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var processExists = await _db.Processes.AnyAsync(p => p.Id == cmd.ProcessId, ct);
        if (!processExists)
        {
            throw new KeyNotFoundException($"Process {cmd.ProcessId} not found.");
        }

        // ArgumentException (end<=start / name) bubbles to the controller → es-CR.
        var window = ProcessEvent.CreateReceptionWindow(
            cmd.ProcessId, cmd.Name, cmd.StartUtc, cmd.EndUtc,
            cmd.ApplicantFacingMessage, cmd.Description, cmd.DisplayOrder, actorUserId);

        _db.ProcessEvents.Add(window);
        await _db.SaveChangesAsync(ct); // assign Id before the audit payload

        await _audit.WriteAsync(
            AdminAuditEvent.ReceptionWindowCreated, actorUserId,
            JsonSerializer.Serialize(new { processId = cmd.ProcessId, windowId = window.Id, name = window.Name }), ct);
        await _db.SaveChangesAsync(ct);

        return window.Id;
    }

    public async Task UpdateAsync(
        UpdateReceptionWindowCommand cmd, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var window = await LoadAsync(cmd.WindowId, ct);
        window.Update(
            cmd.Name, cmd.StartUtc, cmd.EndUtc,
            cmd.ApplicantFacingMessage, cmd.Description, cmd.DisplayOrder, actorUserId);

        await _audit.WriteAsync(
            AdminAuditEvent.ReceptionWindowUpdated, actorUserId,
            JsonSerializer.Serialize(new { processId = window.ProcessId, windowId = window.Id, name = window.Name }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(
        int windowId, bool isActive, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var window = await LoadAsync(windowId, ct);

        // Spec 030 convention — a no-op (already in the requested state) persists
        // nothing and writes no audit row (mirrors ProcessService.RenameAsync).
        if (window.IsActive == isActive)
        {
            return;
        }

        if (isActive)
        {
            window.Activate(actorUserId);
        }
        else
        {
            window.Deactivate(actorUserId);
        }

        await _audit.WriteAsync(
            isActive ? AdminAuditEvent.ReceptionWindowActivated : AdminAuditEvent.ReceptionWindowDeactivated,
            actorUserId,
            JsonSerializer.Serialize(new { processId = window.ProcessId, windowId = window.Id }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int windowId, string actorUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var window = await LoadAsync(windowId, ct);
        var processId = window.ProcessId;
        _db.ProcessEvents.Remove(window);

        await _audit.WriteAsync(
            AdminAuditEvent.ReceptionWindowDeleted, actorUserId,
            JsonSerializer.Serialize(new { processId, windowId }), ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ProcessEvent> LoadAsync(int windowId, CancellationToken ct)
        => await _db.ProcessEvents.FirstOrDefaultAsync(e => e.Id == windowId, ct)
            ?? throw new KeyNotFoundException($"Reception window {windowId} not found.");
}
