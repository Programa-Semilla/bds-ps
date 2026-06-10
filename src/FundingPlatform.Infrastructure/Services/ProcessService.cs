// Spec 021 — see specs/021-feedback-session-may13/tasks.md T077.

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Processes;
using FundingPlatform.Application.Processes.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / US1 / T077 — implements <see cref="IProcessService"/> and
/// <see cref="IProcessQueryService"/>. Every mutation stages an
/// <c>AdminAuditEvent</c> via the spec-021 <see cref="IAdminAuditEventWriter"/>
/// seam and commits in the same UnitOfWork. Mirrors spec-016
/// <c>GroupService</c> for shape and transactional discipline.
/// </summary>
public sealed class ProcessService : IProcessService, IProcessQueryService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;
    // Spec 021 / FR-021 / T152 — the "blocking active applications" listing
    // is an admin surface; soft-deleted Applications must not block a Process
    // close (FR-021 / SC-011).
    private readonly IApplicationQueryFilter _queryFilter;

    public ProcessService(AppDbContext db, IAdminAuditEventWriter audit, IApplicationQueryFilter queryFilter)
    {
        _db = db;
        _audit = audit;
        _queryFilter = queryFilter;
    }

    // -------------------- Commands -----------------------------------------

    public async Task<int> CreateAsync(CreateProcessCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        // Spec 029 / FR-002 / FR-008 — a Process must be anchored to an Active
        // Fund. Reject a missing/Archived Fund before constructing the entity.
        var fund = await _db.Funds.FirstOrDefaultAsync(f => f.Id == command.FundId, ct);
        if (fund is null)
        {
            throw new KeyNotFoundException($"Fund {command.FundId} not found.");
        }
        if (fund.Status != FundStatus.Active)
        {
            throw new InvalidOperationException("Debe seleccionar un fondo activo.");
        }

        var entity = Process.Create(command.Name, command.FundId);

        _db.Processes.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Audit row carries the persisted id in the payload (TargetId derivation
        // in AdminAuditEventWriter only knows the kind-prefix; the controller-side
        // payload is the source of truth for the id of interest).
        await _audit.WriteAsync(
            AdminAuditEvent.ProcessCreated,
            actorUserId,
            JsonSerializer.Serialize(new { processId = entity.Id, name = entity.Name }),
            ct);
        await _db.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task CloseAsync(CloseProcessCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var process = await _db.Processes.FirstOrDefaultAsync(p => p.Id == command.ProcessId, ct)
            ?? throw new KeyNotFoundException($"Process {command.ProcessId} not found.");

        var offenders = await ListBlockingActiveApplicationPublicCodesAsync(command.ProcessId, ct);
        if (offenders.Count > 0)
        {
            throw new ProcessCloseBlockedException(command.ProcessId, offenders);
        }

        process.Close();

        await _audit.WriteAsync(
            AdminAuditEvent.ProcessClosed,
            actorUserId,
            JsonSerializer.Serialize(new { processId = process.Id, closedAt = process.ClosedAt }),
            ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task OverrideStageWindowAsync(
        OverrideStageWindowCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        var process = await _db.Processes.FirstOrDefaultAsync(p => p.Id == command.ProcessId, ct)
            ?? throw new KeyNotFoundException($"Process {command.ProcessId} not found.");

        process.OverrideStageWindow(command.StageKind, command.Days);

        await _audit.WriteAsync(
            AdminAuditEvent.ProcessStageWindowOverridden,
            actorUserId,
            JsonSerializer.Serialize(new
            {
                processId = process.Id,
                stageKind = command.StageKind.ToString(),
                days = command.Days,
            }),
            ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> AssignPlantillaAsync(
        AssignPlantillaCommand command, string actorUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        // Eager-load nav so AssignTo() can see both the existing snapshot and
        // the candidate ImpactTemplates without triggering lazy-load failures.
        var process = await _db.Processes
            .Include(p => p.Plantilla)
            .FirstOrDefaultAsync(p => p.Id == command.ProcessId, ct)
            ?? throw new KeyNotFoundException($"Process {command.ProcessId} not found.");

        var plantilla = await _db.Plantillas
            .Include(p => p.ImpactTemplates)
            .FirstOrDefaultAsync(p => p.Id == command.PlantillaId, ct)
            ?? throw new KeyNotFoundException($"Plantilla {command.PlantillaId} not found.");

        var snapshot = plantilla.AssignTo(process);
        _db.ProcessPlantillas.Add(snapshot);

        await _audit.WriteAsync(
            AdminAuditEvent.PlantillaAssignedToProcess,
            actorUserId,
            JsonSerializer.Serialize(new
            {
                processId = process.Id,
                plantillaId = plantilla.Id,
                snapshotImpactTemplateIds = snapshot.ImpactTemplateIds(),
                snapshotMinimumQuotationsPerItem = snapshot.MinimumQuotationsPerItem,
            }),
            ct);
        await _db.SaveChangesAsync(ct);

        return snapshot.Id;
    }

    public async Task<IReadOnlyList<string>> ListBlockingActiveApplicationPublicCodesAsync(
        int processId, CancellationToken ct)
    {
        // OQ-2 — "Active" Applications are those in any state ≤ AgreementExecuted
        // that have NOT yet finalized. The set Borrador / Submitted / UnderReview /
        // AppealOpen are all Active for close-blocking purposes. ResponseFinalized
        // + AgreementExecuted represent "the cycle has produced its deliverable"
        // and should not block closure — the data-model.md state-transitions block
        // pins the active set explicitly.
        var activeStates = new[]
        {
            ApplicationState.Draft,
            ApplicationState.Submitted,
            ApplicationState.UnderReview,
            ApplicationState.AppealOpen,
        };

        // Cross from Application → Applicant.UserId → UserGroupMembership.UserId
        // → Group.ProcessId, mirroring the spec-016 reviewer-scope predicate
        // shape (kept at the EF query level so SQL Server filters this in one
        // EXISTS join).
        // Spec 021 / FR-021 / T152 — filter the Applications source through
        // ExcludeDeleted so a soft-deleted-but-otherwise-active Application
        // never blocks a Process close.
        var apps = _queryFilter.ExcludeDeleted(_db.Applications.AsNoTracking());
        var codes = await (
            from a in apps
            join applicant in _db.Applicants.AsNoTracking() on a.ApplicantId equals applicant.Id
            where activeStates.Contains(a.State)
            where _db.UserGroupMemberships.AsNoTracking().Any(m =>
                m.UserId == applicant.UserId
                && _db.Groups.AsNoTracking().Any(g => g.Id == m.GroupId && g.ProcessId == processId))
            select a)
            .Select(a => a.PublicCode != null ? a.PublicCode.Value : ("APP-" + a.Id))
            .ToListAsync(ct);

        return codes;
    }

    // -------------------- Queries ------------------------------------------

    public async Task<IReadOnlyList<ProcessListRow>> ListAsync(
        ProcessStatus? statusFilter, CancellationToken ct)
    {
        var query = _db.Processes.AsNoTracking();
        if (statusFilter is { } status)
        {
            query = query.Where(p => p.Status == status);
        }

        // Join the snapshot to expose its source Plantilla name when present.
        var rows = await (
            from p in query
            orderby p.CreatedAt descending
            select new
            {
                p.Id,
                p.Name,
                p.Status,
                p.CreatedAt,
                p.ClosedAt,
                GroupCount = _db.Groups.AsNoTracking().Count(g => g.ProcessId == p.Id),
                PlantillaName = (
                    from pp in _db.ProcessPlantillas.AsNoTracking()
                    join pl in _db.Plantillas.AsNoTracking() on pp.SourcePlantillaId equals pl.Id
                    where pp.ProcessId == p.Id
                    select pl.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new ProcessListRow(
            r.Id, r.Name, r.Status, r.CreatedAt, r.ClosedAt, r.GroupCount, r.PlantillaName)).ToList();
    }

    public async Task<ProcessDetail?> GetDetailAsync(int processId, CancellationToken ct)
    {
        var process = await _db.Processes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (process is null) return null;

        var snapshot = await (
            from pp in _db.ProcessPlantillas.AsNoTracking()
            join pl in _db.Plantillas.AsNoTracking() on pp.SourcePlantillaId equals pl.Id
            where pp.ProcessId == processId
            select new
            {
                pp.Id,
                pp.SourcePlantillaId,
                pl.Name,
                pp.MinimumQuotationsPerItem,
                pp.RequiredFieldFlags,
                pp.ImpactTemplateIdsCsv,
                pp.AssignedAt,
            })
            .FirstOrDefaultAsync(ct);

        ProcessPlantillaSnapshotDto? snapshotDto = null;
        if (snapshot is not null)
        {
            var ids = ParseCsv(snapshot.ImpactTemplateIdsCsv);
            var templateNames = await _db.ImpactTemplates.AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(ct);
            // Preserve the order in the CSV so the admin sees the snapshot's own
            // ordering rather than ImpactTemplate.Id order.
            var nameByIdOrdered = ids
                .Select(id => templateNames.FirstOrDefault(n => n.Id == id)?.Name ?? $"#{id}")
                .ToList();
            snapshotDto = new ProcessPlantillaSnapshotDto(
                snapshot.Id,
                snapshot.SourcePlantillaId,
                snapshot.Name,
                snapshot.MinimumQuotationsPerItem,
                snapshot.RequiredFieldFlags,
                ids,
                nameByIdOrdered,
                snapshot.AssignedAt);
        }

        var groups = await _db.Groups
            .AsNoTracking()
            .Where(g => g.ProcessId == processId)
            .OrderBy(g => g.Name)
            .Select(g => new ProcessGroupRow(g.Id, g.Name, g.Memberships.Count()))
            .ToListAsync(ct);

        return new ProcessDetail(
            process.Id,
            process.Name,
            process.Status,
            process.CreatedAt,
            process.ClosedAt,
            process.SolicitudWindowDays,
            process.RevisionWindowDays,
            process.FacturacionWindowDays,
            snapshotDto,
            groups);
    }

    private static List<int> ParseCsv(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return new();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }
}
