using System.Text.Json;
using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
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
        // Single round-trip: project the count via the configured navigation.
        return await _db.Groups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupRow(g.Id, g.Name, g.Memberships.Count()))
            .ToListAsync(ct);
    }

    public async Task<GroupDetail?> GetAsync(int id, CancellationToken ct)
    {
        var g = await _db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return g is null ? null : new GroupDetail(g.Id, g.Name);
    }

    public async Task<int> CreateAsync(string name, string actorUserId, CancellationToken ct)
    {
        // Domain entity does the trim + length validation. Uniqueness is the
        // unique index on dbo.Groups.Name; we surface DbUpdateException as
        // DuplicateGroupNameException for the controller.
        var entity = Group.Create(name);

        // Pre-check for a friendlier round-trip: if a duplicate is already in
        // the catalog, no need to issue the INSERT at all. The unique index is
        // still the authoritative gate for races between two admins.
        if (await _db.Groups.AnyAsync(g => g.Name == entity.Name, ct))
        {
            throw new DuplicateGroupNameException(entity.Name);
        }

        _db.Groups.Add(entity);
        await _audit.WriteAsync(
            AdminAuditEvent.Record(
                actorUserId,
                AdminAuditEvent.ActionGroupCreate,
                AdminAuditEvent.TargetTypeGroup,
                // TargetId is filled with the persisted Id after SaveChanges
                // by re-stamping the audit row's TargetId via a single tx —
                // simpler approach: write payload-only here and patch after
                // SaveChanges completes by holding a reference.
                "0",
                JsonSerializer.Serialize(new { name = entity.Name })),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateGroupNameException(entity.Name);
        }

        // Patch the audit row's TargetId now that the group has its id. The row
        // is still tracked from the same SaveChanges round-trip, so a second
        // SaveChanges flushes only the updated TargetId.
        var auditRow = _db.AdminAuditEvents.Local
            .Where(e => e.Action == AdminAuditEvent.ActionGroupCreate
                     && e.TargetType == AdminAuditEvent.TargetTypeGroup
                     && e.ActorUserId == actorUserId
                     && e.TargetId == "0")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefault();
        if (auditRow is not null)
        {
            // Reflection-free update via the DbContext's tracked entry.
            _db.Entry(auditRow).Property(nameof(AdminAuditEvent.TargetId)).CurrentValue
                = entity.Id.ToString();
            await _db.SaveChangesAsync(ct);
        }

        return entity.Id;
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
        // SQL Server unique-constraint violation surfaces as Number 2601 or 2627.
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
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
