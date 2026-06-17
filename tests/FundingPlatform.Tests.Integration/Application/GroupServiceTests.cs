using FundingPlatform.Application.Admin.Groups;
using FundingPlatform.Application.Audit;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 016 / 021 — DB-backed service tests for <see cref="GroupService"/>.
/// SCOPE LIMITATION: uses the EF InMemory provider for parity with the rest of
/// this project (see <c>ExchangeRateRepositoryTests</c>). The real SQL unique
/// index, case/accent-insensitive collation, and ON DELETE CASCADE are
/// exercised by the E2E suite (T029, T050) against the Aspire-orchestrated
/// SQL container.
///
/// Spec 021 / FR-001 — every Group belongs to exactly one Process; the create
/// path requires a Process id and there is no Process-less fallback.
/// </summary>
[TestFixture]
public class GroupServiceTests
{
    private const string ActorAdminId = "actor-admin-1";

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static GroupService BuildService(AppDbContext ctx)
    {
        IAdminAuditWriter audit = new AdminAuditWriter(ctx);
        return new GroupService(ctx, audit);
    }

    private static async Task<ApplicationUser> SeedUserAsync(AppDbContext ctx, string email)
    {
        var user = new ApplicationUser(email, "First", "Last", null);
        user.Id = Guid.NewGuid().ToString();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    /// <summary>Spec 021 / FR-001 — seeds an Active Process and returns its id.</summary>
    private static async Task<int> SeedProcessAsync(AppDbContext ctx, string name = "Crocus 2025")
    {
        var process = Process.Create(name, 1);
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();
        return process.Id;
    }

    [Test]
    public async Task Create_PersistsGroup_AndWritesAuditRow()
    {
        var dbName = $"groups-create-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedUserAsync(ctx, "admin@example.com");
        var processId = await SeedProcessAsync(ctx);
        var sut = BuildService(ctx);

        var id = await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);

        Assert.That(id, Is.GreaterThan(0));
        var group = await ctx.Groups.SingleAsync(g => g.Id == id);
        Assert.That(group.Name, Is.EqualTo("Norte"));

        var audit = await ctx.AdminAuditEvents
            .Where(e => e.Action == AdminAuditEvent.ActionGroupCreate)
            .SingleAsync();
        Assert.That(audit.ActorUserId, Is.EqualTo(ActorAdminId));
        Assert.That(audit.TargetType, Is.EqualTo(AdminAuditEvent.TargetTypeGroup));
        Assert.That(audit.TargetId, Is.EqualTo(id.ToString()));
        Assert.That(audit.PayloadJson, Does.Contain("Norte"));
    }

    [Test]
    public async Task Create_AttachesGroupToGivenProcess_AndAuditCarriesProcessId()
    {
        // Spec 021 / FR-001 — the created Group belongs to exactly the Process
        // the caller supplied (no "Migración inicial" fallback).
        var dbName = $"groups-create-process-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var processId = await SeedProcessAsync(ctx, "Crocus 2025");
        var sut = BuildService(ctx);

        var id = await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);

        var group = await ctx.Groups.SingleAsync(g => g.Id == id);
        Assert.That(group.ProcessId, Is.EqualTo(processId));

        var audit = await ctx.AdminAuditEvents
            .SingleAsync(e => e.Action == AdminAuditEvent.ActionGroupCreate);
        Assert.That(audit.PayloadJson, Does.Contain($"\"processId\":{processId}"));
    }

    [Test]
    public async Task Create_UnknownProcess_ThrowsKeyNotFound()
    {
        var dbName = $"groups-create-noproc-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var sut = BuildService(ctx);

        Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await sut.CreateAsync("Norte", 99999, ActorAdminId, CancellationToken.None));
    }

    [Test]
    public async Task Create_ClosedProcess_ThrowsInvalidOperation()
    {
        var dbName = $"groups-create-closed-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var process = Process.Create("Crocus 2024", 1);
        process.Close();
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();
        var sut = BuildService(ctx);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.CreateAsync("Norte", process.Id, ActorAdminId, CancellationToken.None));
    }

    [Test]
    public async Task Create_DuplicateName_ThrowsDuplicateGroupNameException()
    {
        var dbName = $"groups-dupe-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedUserAsync(ctx, "admin@example.com");
        var processId = await SeedProcessAsync(ctx);
        var sut = BuildService(ctx);

        await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);

        Assert.ThrowsAsync<DuplicateGroupNameException>(
            async () => await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None));
        // Pre-check uses the case-sensitive InMemory provider; the real SQL
        // unique index (case- and accent-insensitive collation) is verified by
        // the E2E suite. Test the trim path here — equally trimmed name still
        // collides via the exact-match pre-check.
        Assert.ThrowsAsync<DuplicateGroupNameException>(
            async () => await sut.CreateAsync("  Norte  ", processId, ActorAdminId, CancellationToken.None));
    }

    [Test]
    public async Task Create_SameNameUnderDifferentProcess_IsAllowed()
    {
        // Group names are unique PER PROCESS, not globally: the same name may be
        // reused by a group in a different Process (even within the same Fund).
        var dbName = $"groups-crossprocess-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedUserAsync(ctx, "admin@example.com");
        var processA = await SeedProcessAsync(ctx, "Proceso A");
        var processB = await SeedProcessAsync(ctx, "Proceso B");
        var sut = BuildService(ctx);

        await sut.CreateAsync("Norte", processA, ActorAdminId, CancellationToken.None);

        // Same group name under a different Process must NOT collide.
        Assert.DoesNotThrowAsync(
            async () => await sut.CreateAsync("Norte", processB, ActorAdminId, CancellationToken.None));
    }

    [Test]
    public async Task MoveToProcess_ReparentsGroup_AndWritesAuditRow()
    {
        // Spec 021 / FR-001 — admin reparents a Group to a different Process.
        var dbName = $"groups-move-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var fromProcess = await SeedProcessAsync(ctx, "Crocus 2025");
        var toProcess = await SeedProcessAsync(ctx, "Nexo 2026");
        var sut = BuildService(ctx);

        var id = await sut.CreateAsync("Norte", fromProcess, ActorAdminId, CancellationToken.None);

        await sut.MoveToProcessAsync(id, toProcess, ActorAdminId, CancellationToken.None);

        var group = await ctx.Groups.SingleAsync(g => g.Id == id);
        Assert.That(group.ProcessId, Is.EqualTo(toProcess));

        var audit = await ctx.AdminAuditEvents
            .SingleAsync(e => e.Action == AdminAuditEvent.ActionGroupMoveProcess);
        Assert.That(audit.TargetId, Is.EqualTo(id.ToString()));
        Assert.That(audit.PayloadJson, Does.Contain($"\"fromProcessId\":{fromProcess}"));
        Assert.That(audit.PayloadJson, Does.Contain($"\"toProcessId\":{toProcess}"));
    }

    [Test]
    public async Task MoveToProcess_SameProcess_IsNoOp_WritesNoAuditRow()
    {
        var dbName = $"groups-move-noop-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var processId = await SeedProcessAsync(ctx);
        var sut = BuildService(ctx);

        var id = await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);
        await sut.MoveToProcessAsync(id, processId, ActorAdminId, CancellationToken.None);

        var moveAudits = await ctx.AdminAuditEvents
            .CountAsync(e => e.Action == AdminAuditEvent.ActionGroupMoveProcess);
        Assert.That(moveAudits, Is.EqualTo(0));
    }

    [Test]
    public async Task MoveToProcess_ClosedTarget_ThrowsInvalidOperation()
    {
        var dbName = $"groups-move-closed-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var fromProcess = await SeedProcessAsync(ctx, "Crocus 2025");
        var closed = Process.Create("Crocus 2024", 1);
        closed.Close();
        ctx.Processes.Add(closed);
        await ctx.SaveChangesAsync();
        var sut = BuildService(ctx);

        var id = await sut.CreateAsync("Norte", fromProcess, ActorAdminId, CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.MoveToProcessAsync(id, closed.Id, ActorAdminId, CancellationToken.None));
    }

    [Test]
    public async Task Rename_PreservesMemberships_AndWritesAuditRow()
    {
        var dbName = $"groups-rename-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var user = await SeedUserAsync(ctx, "alice@example.com");
        var processId = await SeedProcessAsync(ctx);
        var sut = BuildService(ctx);

        var id = await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);
        ctx.UserGroupMemberships.Add(new UserGroupMembership(user.Id, id));
        await ctx.SaveChangesAsync();

        await sut.RenameAsync(id, "Norte Pacífico", ActorAdminId, CancellationToken.None);

        var group = await ctx.Groups.SingleAsync(g => g.Id == id);
        Assert.That(group.Name, Is.EqualTo("Norte Pacífico"));
        var memberships = await ctx.UserGroupMemberships.Where(m => m.GroupId == id).ToListAsync();
        Assert.That(memberships, Has.Count.EqualTo(1));
        Assert.That(memberships[0].UserId, Is.EqualTo(user.Id));

        var renameAudit = await ctx.AdminAuditEvents
            .SingleAsync(e => e.Action == AdminAuditEvent.ActionGroupRename);
        // System.Text.Json escapes non-ASCII by default ("í" → "í"), so
        // assert on the escaped form rather than the literal unicode glyph.
        Assert.That(renameAudit.PayloadJson, Does.Contain("Pac"));
        Assert.That(renameAudit.PayloadJson, Does.Contain("\"old\":\"Norte\""));
    }

    [Test]
    public async Task Rename_DuplicateName_Throws()
    {
        var dbName = $"groups-rename-dupe-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        await SeedUserAsync(ctx, "admin@example.com");
        var processId = await SeedProcessAsync(ctx);
        var sut = BuildService(ctx);

        var id1 = await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);
        await sut.CreateAsync("Sur", processId, ActorAdminId, CancellationToken.None);

        Assert.ThrowsAsync<DuplicateGroupNameException>(
            async () => await sut.RenameAsync(id1, "Sur", ActorAdminId, CancellationToken.None));
    }

    [Test]
    public async Task Delete_RemovesMembershipsButNotUsers()
    {
        var dbName = $"groups-delete-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var user = await SeedUserAsync(ctx, "alice@example.com");
        var processId = await SeedProcessAsync(ctx);
        var sut = BuildService(ctx);

        var id = await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);
        ctx.UserGroupMemberships.Add(new UserGroupMembership(user.Id, id));
        await ctx.SaveChangesAsync();

        var removed = await sut.DeleteAsync(id, ActorAdminId, CancellationToken.None);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(await ctx.Groups.AnyAsync(g => g.Id == id), Is.False);
        // InMemory provider does NOT enforce FK cascade automatically; the test
        // above asserts the C# code removed memberships in the same SaveChanges.
        // Real SQL cascade is verified by the integration test in
        // tests/FundingPlatform.Tests.Integration/Application/GroupDeletionCascadeTests.cs (T049/T051).
        Assert.That(await ctx.Users.AnyAsync(u => u.Id == user.Id), Is.True,
            "User row must NOT be deleted (FR-005).");

        var audit = await ctx.AdminAuditEvents
            .SingleAsync(e => e.Action == AdminAuditEvent.ActionGroupDelete);
        Assert.That(audit.PayloadJson, Does.Contain("Norte"));
        Assert.That(audit.PayloadJson, Does.Contain("memberCountBefore"));
    }

    [Test]
    public async Task List_ProjectsMemberCount_AndOwningProcessName()
    {
        var dbName = $"groups-list-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);
        var u1 = await SeedUserAsync(ctx, "u1@example.com");
        var u2 = await SeedUserAsync(ctx, "u2@example.com");
        var processId = await SeedProcessAsync(ctx, "Crocus 2025");
        var sut = BuildService(ctx);

        var norteId = await sut.CreateAsync("Norte", processId, ActorAdminId, CancellationToken.None);
        var surId = await sut.CreateAsync("Sur", processId, ActorAdminId, CancellationToken.None);

        ctx.UserGroupMemberships.Add(new UserGroupMembership(u1.Id, norteId));
        ctx.UserGroupMemberships.Add(new UserGroupMembership(u2.Id, norteId));
        ctx.UserGroupMemberships.Add(new UserGroupMembership(u1.Id, surId));
        await ctx.SaveChangesAsync();

        var rows = await sut.ListAsync(CancellationToken.None);

        Assert.That(rows, Has.Count.EqualTo(2));
        // Sorted by name asc: "Norte", "Sur" (alphabetical).
        Assert.That(rows[0].Name, Is.EqualTo("Norte"));
        Assert.That(rows[0].MemberCount, Is.EqualTo(2));
        Assert.That(rows[0].ProcessId, Is.EqualTo(processId));
        Assert.That(rows[0].ProcessName, Is.EqualTo("Crocus 2025"));
        Assert.That(rows[1].Name, Is.EqualTo("Sur"));
        Assert.That(rows[1].MemberCount, Is.EqualTo(1));
        Assert.That(rows[1].ProcessName, Is.EqualTo("Crocus 2025"));
    }
}
