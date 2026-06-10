using System.Text.Json;
using FundingPlatform.Application.Processes;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 030 / FR-003 / FR-006 (T005) — <see cref="ProcessService.RenameAsync"/>
/// persistence + audit behavior against an EF context (mirrors the spec-029
/// <c>FundServiceTests</c> InMemory pattern).
///
/// NOTE: the duplicate-name path (FR-005) surfaces as a <c>DbUpdateException</c>
/// from the <c>UX_Processes_Name</c> unique index, and the concurrent-collision
/// edge case (spec Edge Cases) relies on that same index plus the
/// <c>RowVersion</c> token. The EF InMemory provider enforces neither, so the
/// (sequential) duplicate path is exercised end-to-end against the real
/// dacpac-deployed SQL Server in the E2E suite (<c>RenameProcessTests</c>); a
/// deterministic concurrent-collision test is not reproducible here.
/// </summary>
[TestFixture]
public class ProcessRenameServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ProcessService NewService(AppDbContext ctx) =>
        new(ctx, new AdminAuditEventWriter(ctx), new ApplicationQueryFilter());

    private const string Actor = "admin-user-1";

    private static async Task<int> SeedProcessAsync(AppDbContext ctx, string name)
    {
        var process = Process.Create(name, 1);
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();
        return process.Id;
    }

    [Test]
    public async Task Rename_HappyPath_PersistsNewName_AndWritesOneAuditRow()
    {
        var db = $"proc-rename-ok-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
            id = await SeedProcessAsync(ctx, "Crocus 2025");

        using (var ctx = CreateContext(db))
            await NewService(ctx).RenameAsync(
                new RenameProcessCommand(id, "Crocus 2025-II"), Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            var loaded = await ctx.Processes.FirstAsync(p => p.Id == id);
            Assert.That(loaded.Name, Is.EqualTo("Crocus 2025-II"));

            var audit = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.ProcessRenamed
                    && a.TargetType == AdminAuditEvent.TargetTypeProcess)
                .ToListAsync();
            Assert.That(audit, Has.Count.EqualTo(1), "Exactly one process.renamed row.");
            Assert.That(audit[0].ActorUserId, Is.EqualTo(Actor));

            using var payload = JsonDocument.Parse(audit[0].PayloadJson!);
            Assert.That(payload.RootElement.GetProperty("processId").GetInt32(), Is.EqualTo(id));
            Assert.That(payload.RootElement.GetProperty("oldName").GetString(), Is.EqualTo("Crocus 2025"));
            Assert.That(payload.RootElement.GetProperty("newName").GetString(), Is.EqualTo("Crocus 2025-II"));
        }
    }

    [Test]
    public async Task Rename_SameName_IsNoOp_WritesNoAuditRow()
    {
        var db = $"proc-rename-noop-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
            id = await SeedProcessAsync(ctx, "Crocus 2025");

        using (var ctx = CreateContext(db))
            await NewService(ctx).RenameAsync(
                new RenameProcessCommand(id, "Crocus 2025"), Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            Assert.That((await ctx.Processes.FirstAsync(p => p.Id == id)).Name, Is.EqualTo("Crocus 2025"));
            var audit = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.ProcessRenamed)
                .ToListAsync();
            Assert.That(audit, Is.Empty, "A no-op rename must write no audit row (FR-006 / SC-005).");
        }
    }

    [Test]
    public async Task Rename_TrimmedSameName_IsNoOp_WritesNoAuditRow()
    {
        var db = $"proc-rename-noop-trim-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
            id = await SeedProcessAsync(ctx, "Crocus 2025");

        using (var ctx = CreateContext(db))
            await NewService(ctx).RenameAsync(
                new RenameProcessCommand(id, "  Crocus 2025  "), Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            Assert.That((await ctx.Processes.FirstAsync(p => p.Id == id)).Name, Is.EqualTo("Crocus 2025"));
            Assert.That(
                await ctx.AdminAuditEvents.AnyAsync(a => a.Action == AdminAuditEvent.ProcessRenamed),
                Is.False);
        }
    }

    [Test]
    public void Rename_UnknownId_ThrowsKeyNotFound()
    {
        var db = $"proc-rename-404-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);

        Assert.ThrowsAsync<KeyNotFoundException>(() =>
            NewService(ctx).RenameAsync(
                new RenameProcessCommand(999_999, "Nuevo"), Actor, CancellationToken.None));
    }

    [Test]
    public async Task Rename_OverLength_Throws_PersistsNothing()
    {
        var db = $"proc-rename-overlen-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
            id = await SeedProcessAsync(ctx, "Crocus 2025");

        var overLength = new string('x', Process.MaxNameLength + 1); // 121

        using (var ctx = CreateContext(db))
            Assert.ThrowsAsync<ArgumentException>(() =>
                NewService(ctx).RenameAsync(
                    new RenameProcessCommand(id, overLength), Actor, CancellationToken.None));

        using (var ctx = CreateContext(db))
        {
            Assert.That((await ctx.Processes.FirstAsync(p => p.Id == id)).Name, Is.EqualTo("Crocus 2025"),
                "Over-length rename must not change the stored name (FR-004 / SC-003).");
            Assert.That(
                await ctx.AdminAuditEvents.AnyAsync(a => a.Action == AdminAuditEvent.ProcessRenamed),
                Is.False, "A rejected rename must write no audit row.");
        }
    }

    [Test]
    public async Task Rename_ClosedProcess_PersistsName_AndWritesAuditRow()
    {
        // Spec 030 / FR-002 / FR-003 / SC-004 — rename (and its audit) succeed on a
        // Closed Process exactly as on an Active one (no status guard).
        var db = $"proc-rename-closed-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
        {
            var process = Process.Create("Nexo 2025", 1);
            process.Close();
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();
            id = process.Id;
        }

        using (var ctx = CreateContext(db))
            await NewService(ctx).RenameAsync(
                new RenameProcessCommand(id, "Nexo 2025-II"), Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            var loaded = await ctx.Processes.FirstAsync(p => p.Id == id);
            Assert.That(loaded.Name, Is.EqualTo("Nexo 2025-II"));
            Assert.That(loaded.Status, Is.EqualTo(FundingPlatform.Domain.Enums.ProcessStatus.Closed),
                "Rename must not change the Closed status.");
            Assert.That(
                await ctx.AdminAuditEvents.CountAsync(a => a.Action == AdminAuditEvent.ProcessRenamed),
                Is.EqualTo(1), "A Closed-Process rename still writes exactly one audit row (FR-003).");
        }
    }
}
