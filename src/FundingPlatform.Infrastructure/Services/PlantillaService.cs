// Spec 021 — see specs/021-feedback-session-may13/tasks.md T078.

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Plantillas;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / US1 / T078 — implements <see cref="IPlantillaService"/>. Force-detach
/// is the only mutation that writes an audit row (per OQ-9 — base-Plantilla edits
/// are catalog ops; only the snapshot-life-cycle gets audited). Mirrors the
/// spec-016 <c>GroupService</c> shape for transaction discipline.
/// </summary>
public sealed class PlantillaService : IPlantillaService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;

    public PlantillaService(AppDbContext db, IAdminAuditEventWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PlantillaListRow>> ListAsync(CancellationToken ct)
    {
        // We carry the count of attached ImpactTemplates AND the count of
        // ProcessPlantilla snapshots that reference the base Plantilla — the
        // latter drives the "force-archive" gate.
        var rows = await (
            from p in _db.Plantillas.AsNoTracking()
            orderby p.IsArchived, p.Name
            select new
            {
                p.Id,
                p.Name,
                p.MinimumQuotationsPerItem,
                ImpactTemplateCount = p.ImpactTemplates.Count(),
                AssignedProcessCount = _db.ProcessPlantillas
                    .AsNoTracking()
                    .Count(pp => pp.SourcePlantillaId == p.Id),
                p.IsArchived,
                p.CreatedAt,
            })
            .ToListAsync(ct);

        return rows.Select(r => new PlantillaListRow(
            r.Id, r.Name, r.MinimumQuotationsPerItem,
            r.ImpactTemplateCount, r.AssignedProcessCount,
            r.IsArchived, r.CreatedAt)).ToList();
    }

    public async Task<PlantillaDetail?> GetAsync(int id, CancellationToken ct)
    {
        var plantilla = await _db.Plantillas
            .AsNoTracking()
            .Include(p => p.ImpactTemplates)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plantilla is null) return null;

        var assignedCount = await _db.ProcessPlantillas
            .AsNoTracking()
            .CountAsync(pp => pp.SourcePlantillaId == id, ct);

        return new PlantillaDetail(
            plantilla.Id,
            plantilla.Name,
            plantilla.MinimumQuotationsPerItem,
            plantilla.RequiredFieldFlags,
            plantilla.IsArchived,
            plantilla.ImpactTemplates.Select(t => t.Id).OrderBy(i => i).ToList(),
            assignedCount);
    }

    public async Task<int> CreateAsync(CreatePlantillaCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var entity = Plantilla.Create(command.Name, command.MinimumQuotationsPerItem, command.RequiredFieldFlags);

        if (command.ImpactTemplateIds is { Count: > 0 })
        {
            // Materialize the chosen templates and attach so the EF many-to-many
            // join is populated in the same UnitOfWork.
            var templates = await _db.ImpactTemplates
                .Where(t => command.ImpactTemplateIds.Contains(t.Id))
                .ToListAsync(ct);
            foreach (var t in templates)
            {
                entity.AttachImpactTemplate(t);
            }
        }

        _db.Plantillas.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task EditAsync(EditPlantillaCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var plantilla = await _db.Plantillas
            .Include(p => p.ImpactTemplates)
            .FirstOrDefaultAsync(p => p.Id == command.PlantillaId, ct)
            ?? throw new KeyNotFoundException($"Plantilla {command.PlantillaId} not found.");

        plantilla.Edit(command.Name, command.MinimumQuotationsPerItem, command.RequiredFieldFlags);

        // Reconcile the many-to-many: detach removed, attach added. We compare
        // the snapshot of ids vs the requested ids; the EF tracker handles the
        // join-table side.
        var desired = command.ImpactTemplateIds.ToHashSet();
        var current = plantilla.ImpactTemplates.Select(t => t.Id).ToHashSet();

        foreach (var id in current.Except(desired).ToList())
        {
            plantilla.DetachImpactTemplate(id);
        }
        var toAddIds = desired.Except(current).ToList();
        if (toAddIds.Count > 0)
        {
            var toAdd = await _db.ImpactTemplates
                .Where(t => toAddIds.Contains(t.Id))
                .ToListAsync(ct);
            foreach (var t in toAdd)
            {
                plantilla.AttachImpactTemplate(t);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DetachAsync(DetachPlantillaCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var process = await _db.Processes
            .Include(p => p.Plantilla)
            .FirstOrDefaultAsync(p => p.Id == command.ProcessId, ct)
            ?? throw new KeyNotFoundException($"Process {command.ProcessId} not found.");

        var plantilla = await _db.Plantillas.FirstOrDefaultAsync(p => p.Id == command.PlantillaId, ct)
            ?? throw new KeyNotFoundException($"Plantilla {command.PlantillaId} not found.");

        if (process.Plantilla is null)
        {
            throw new InvalidOperationException(
                $"Process {process.Id} has no Plantilla snapshot to detach.");
        }

        // Without force=true, block when active Applications exist on this
        // Process — they were drafted against this snapshot's
        // MinimumQuotationsPerItem and ImpactTemplate set; ripping the snapshot
        // out behind them would corrupt the in-flight UX.
        if (!command.Force)
        {
            var activeCount = await CountActiveApplicationsAsync(command.ProcessId, ct);
            if (activeCount > 0)
            {
                throw new PlantillaDetachBlockedException(command.PlantillaId, command.ProcessId, activeCount);
            }
        }
        else if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ArgumentException(
                "A non-empty reason is required when force=true.", nameof(command));
        }

        var snapshot = process.Plantilla;
        plantilla.Detach(process, command.Force, command.Reason);
        _db.ProcessPlantillas.Remove(snapshot);

        await _audit.WriteAsync(
            AdminAuditEvent.PlantillaForceDetached,
            actorUserId,
            JsonSerializer.Serialize(new
            {
                processId = process.Id,
                plantillaId = plantilla.Id,
                force = command.Force,
                reason = command.Reason,
            }),
            ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(ArchivePlantillaCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var plantilla = await _db.Plantillas
            .FirstOrDefaultAsync(p => p.Id == command.PlantillaId, ct)
            ?? throw new KeyNotFoundException($"Plantilla {command.PlantillaId} not found.");

        var snapshotCount = await _db.ProcessPlantillas
            .CountAsync(pp => pp.SourcePlantillaId == command.PlantillaId, ct);
        if (snapshotCount > 0)
        {
            throw new InvalidOperationException(
                $"Plantilla {command.PlantillaId} is referenced by {snapshotCount} active ProcessPlantilla snapshot(s).");
        }

        plantilla.Archive();
        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> CountActiveApplicationsAsync(int processId, CancellationToken ct)
    {
        var activeStates = new[]
        {
            ApplicationState.Draft,
            ApplicationState.Submitted,
            ApplicationState.UnderReview,
            ApplicationState.AppealOpen,
        };

        return await (
            from a in _db.Applications.AsNoTracking()
            join applicant in _db.Applicants.AsNoTracking() on a.ApplicantId equals applicant.Id
            where activeStates.Contains(a.State)
            where _db.UserGroupMemberships.AsNoTracking().Any(m =>
                m.UserId == applicant.UserId
                && _db.Groups.AsNoTracking().Any(g => g.Id == m.GroupId && g.ProcessId == processId))
            select a)
            .CountAsync(ct);
    }
}
