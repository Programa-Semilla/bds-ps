using System.Text.Json;
using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 016 — implements <see cref="IGroupService"/>. Each mutation writes
/// exactly one <see cref="AdminAuditEvent"/> via <see cref="IAdminAuditWriter"/>
/// (NFR-005). Unique-name violations on insert/update surface as
/// <see cref="DuplicateGroupNameException"/> so the controller can render a
/// `ModelState` error.
/// </summary>
public sealed class GroupService : IGroupService
{
    private readonly AppDbContext _db;
    private readonly IAdminAuditWriter _audit;

    public GroupService(AppDbContext db, IAdminAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<GroupRow>> ListAsync(CancellationToken ct)
    {
        // Single round-trip: project the member count via the configured
        // navigation and the owning Process name via a correlated subquery
        // (FR-001 — every Group belongs to exactly one Process).
        return await _db.Groups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupRow(
                g.Id,
                g.Name,
                g.Memberships.Count(),
                g.ProcessId,
                _db.Processes.Where(p => p.Id == g.ProcessId).Select(p => p.Name).FirstOrDefault() ?? ""))
            .ToListAsync(ct);
    }

    public async Task<GroupDetail?> GetAsync(int id, CancellationToken ct)
    {
        var g = await _db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return g is null ? null : new GroupDetail(g.Id, g.Name, g.ProcessId);
    }

    public async Task<int> CreateAsync(string name, int processId, string actorUserId, CancellationToken ct)
    {
        // Spec 021 / FR-001 — every Group is attached to exactly one Process
        // (FK_Groups_Processes). The owning Process is supplied by the caller —
        // the Process Details "Nuevo grupo" form passes the route's id — so
        // there is no bootstrap/"Migración inicial" fallback anymore.
        var process = await _db.Processes
            .FirstOrDefaultAsync(p => p.Id == processId, ct)
            ?? throw new KeyNotFoundException($"Process {processId} not found.");
        if (process.Status == ProcessStatus.Closed)
        {
            throw new InvalidOperationException("No se pueden crear grupos en un proceso cerrado.");
        }

        // Domain entity does the trim + length validation. Uniqueness is the
        // unique index on dbo.Groups.Name; we surface DbUpdateException as
        // DuplicateGroupNameException for the controller.
        var entity = Group.Create(name, processId);

        // Pre-check for a friendlier round-trip: if a duplicate is already in
        // the catalog, no need to issue the INSERT at all. The unique index is
        // still the authoritative gate for races between two admins.
        if (await _db.Groups.AnyAsync(g => g.Name == entity.Name, ct))
        {
            throw new DuplicateGroupNameException(entity.Name);
        }

        // Phase 1: persist the Group so its IDENTITY id is available. This is
        // simpler than the previous two-phase pattern (insert audit with
        // TargetId="0", patch after SaveChanges) — the failure modes are now
        // either (a) Group not persisted, no audit row, or (b) Group persisted,
        // audit row missing, instead of (c) audit row with TargetId="0" left
        // behind on a partial second SaveChanges.
        _db.Groups.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateGroupNameException(entity.Name);
        }

        // Phase 2: write the audit row with the persisted id. NFR-005 — every
        // successful mutation MUST have a corresponding audit row; if this
        // SaveChanges fails the audit drift is observable and the operator can
        // back-fill (the Group exists in the catalog, the actor + payload
        // would have been derivable from logs).
        await _audit.WriteAsync(
            AdminAuditEvent.Record(
                actorUserId,
                AdminAuditEvent.ActionGroupCreate,
                AdminAuditEvent.TargetTypeGroup,
                entity.Id.ToString(),
                JsonSerializer.Serialize(new { name = entity.Name, processId })),
            ct);
        await _db.SaveChangesAsync(ct);

        return entity.Id;
    }

    public async Task MoveToProcessAsync(int id, int newProcessId, string actorUserId, CancellationToken ct)
    {
        var group = await _db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new KeyNotFoundException($"Group {id} not found.");

        var fromProcessId = group.ProcessId;
        // Idempotent — a no-op reparent writes no audit row (mirrors Rename).
        if (fromProcessId == newProcessId)
        {
            return;
        }

        var target = await _db.Processes.FirstOrDefaultAsync(p => p.Id == newProcessId, ct)
            ?? throw new KeyNotFoundException($"Process {newProcessId} not found.");
        if (target.Status == ProcessStatus.Closed)
        {
            throw new InvalidOperationException("No se puede mover un grupo a un proceso cerrado.");
        }

        group.MoveToProcess(newProcessId);

        await _audit.WriteAsync(
            AdminAuditEvent.Record(
                actorUserId,
                AdminAuditEvent.ActionGroupMoveProcess,
                AdminAuditEvent.TargetTypeGroup,
                group.Id.ToString(),
                JsonSerializer.Serialize(new { groupId = group.Id, fromProcessId, toProcessId = newProcessId })),
            ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RenameAsync(int id, string newName, string actorUserId, CancellationToken ct)
    {
        var g = await _db.Groups.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"Group {id} not found.");

        var oldName = g.Name;
        // Use the domain method (validates + bumps UpdatedAt; idempotent if equal).
        g.Rename(newName);

        // Uniqueness pre-check excluding self (FR-001 + contracts/admin-groups.md).
        if (!string.Equals(oldName, g.Name, StringComparison.Ordinal)
            && await _db.Groups.AnyAsync(x => x.Id != id && x.Name == g.Name, ct))
        {
            throw new DuplicateGroupNameException(g.Name);
        }

        await _audit.WriteAsync(
            AdminAuditEvent.Record(
                actorUserId,
                AdminAuditEvent.ActionGroupRename,
                AdminAuditEvent.TargetTypeGroup,
                g.Id.ToString(),
                JsonSerializer.Serialize(new { old = oldName, @new = g.Name })),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateGroupNameException(g.Name);
        }
    }

    public async Task<int> DeleteAsync(int id, string actorUserId, CancellationToken ct)
    {
        var g = await _db.Groups
            .Include(x => x.Memberships)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"Group {id} not found.");

        var memberCountBefore = g.Memberships.Count;
        var deletedName = g.Name;

        _db.Groups.Remove(g);
        await _audit.WriteAsync(
            AdminAuditEvent.Record(
                actorUserId,
                AdminAuditEvent.ActionGroupDelete,
                AdminAuditEvent.TargetTypeGroup,
                id.ToString(),
                JsonSerializer.Serialize(new { name = deletedName, memberCountBefore })),
            ct);

        await _db.SaveChangesAsync(ct);
        return memberCountBefore;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // SQL Server unique-constraint violation surfaces as Number 2601
        // (unique-index) or 2627 (unique-constraint). Match on the SqlException
        // Number when available — robust to message localization or index
        // renames. Fall back to message-substring match for non-SQL-Server
        // providers (none in production today; this guards against silently
        // breaking if the provider changes).
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            // Use reflection to read SqlException.Number without taking a hard
            // dependency on Microsoft.Data.SqlClient at this assembly boundary.
            var numberProp = inner.GetType().GetProperty("Number");
            if (numberProp?.GetValue(inner) is int n && (n == 2601 || n == 2627))
            {
                return true;
            }
            var msg = inner.Message;
            if (msg.Contains("UX_Groups_Name", StringComparison.Ordinal)
                || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
