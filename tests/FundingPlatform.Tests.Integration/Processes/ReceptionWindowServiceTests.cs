using System.Text.Json;
using FundingPlatform.Application.Processes.ReceptionWindows;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Processes;

/// <summary>
/// Spec 044 / US1 (T023) — <see cref="ReceptionWindowService"/> CRUD + audit
/// behavior against an EF context (mirrors the spec-030 <c>ProcessRenameServiceTests</c>
/// / spec-029 <c>FundServiceTests</c> InMemory pattern). The CK_EndAfterStart SQL
/// constraint + real-SQL TINYINT materialization are exercised by the E2E suite
/// (<c>ReceptionWindowAdminTests</c>); the domain <c>end&lt;=start</c> rejection is
/// asserted here.
/// </summary>
[TestFixture]
public class ReceptionWindowServiceTests
{
    private const string Actor = "admin-user-1";
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 4, 1, 6, 0, 0, TimeSpan.Zero);

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ReceptionWindowService NewService(AppDbContext ctx) =>
        new(ctx, new AdminAuditEventWriter(ctx));

    private static async Task<int> SeedProcessAsync(AppDbContext ctx)
    {
        var process = Process.Create("Crocus 2026", 1);
        ctx.Processes.Add(process);
        await ctx.SaveChangesAsync();
        return process.Id;
    }

    [Test]
    public async Task Create_PersistsWindow_AndWritesAuditRow()
    {
        var db = $"rw-create-{Guid.NewGuid():N}";
        int processId, windowId;
        using (var ctx = CreateContext(db))
            processId = await SeedProcessAsync(ctx);

        using (var ctx = CreateContext(db))
            windowId = await NewService(ctx).CreateAsync(
                new CreateReceptionWindowCommand(processId, "Primera ventana", Start, End, "¡Pronto!", null, 0),
                Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            var w = await ctx.ProcessEvents.FirstAsync(e => e.Id == windowId);
            Assert.That(w.Name, Is.EqualTo("Primera ventana"));
            Assert.That(w.EventType, Is.EqualTo(ProcessEventType.ReceptionWindow));
            Assert.That(w.ControlsSubmissionAvailability, Is.True);
            Assert.That(w.IsActive, Is.True);

            var audit = await ctx.AdminAuditEvents
                .Where(a => a.Action == AdminAuditEvent.ReceptionWindowCreated
                    && a.TargetType == AdminAuditEvent.TargetTypeProcess)
                .ToListAsync();
            Assert.That(audit, Has.Count.EqualTo(1));
            using var payload = JsonDocument.Parse(audit[0].PayloadJson!);
            Assert.That(payload.RootElement.GetProperty("windowId").GetInt32(), Is.EqualTo(windowId));
        }
    }

    [Test]
    public void Create_EndNotAfterStart_Throws()
    {
        var db = $"rw-badrange-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var processId = SeedProcessAsync(ctx).GetAwaiter().GetResult();

        var ex = Assert.ThrowsAsync<ArgumentException>(() =>
            NewService(ctx).CreateAsync(
                new CreateReceptionWindowCommand(processId, "Mala", End, Start, null, null, 0),
                Actor, CancellationToken.None));
        Assert.That(ex!.ParamName, Is.EqualTo("endUtc"));
    }

    [Test]
    public async Task Update_ChangesFields_AndWritesAuditRow()
    {
        var db = $"rw-update-{Guid.NewGuid():N}";
        int processId, windowId;
        using (var ctx = CreateContext(db))
        {
            processId = await SeedProcessAsync(ctx);
            windowId = await NewService(ctx).CreateAsync(
                new CreateReceptionWindowCommand(processId, "Inicial", Start, End, null, null, 0),
                Actor, CancellationToken.None);
        }

        using (var ctx = CreateContext(db))
            await NewService(ctx).UpdateAsync(
                new UpdateReceptionWindowCommand(windowId, "Renombrada", Start, End.AddDays(10), null, null, 1),
                Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            var w = await ctx.ProcessEvents.FirstAsync(e => e.Id == windowId);
            Assert.That(w.Name, Is.EqualTo("Renombrada"));
            Assert.That(w.EndUtc, Is.EqualTo(End.AddDays(10)));
            Assert.That(await ctx.AdminAuditEvents.CountAsync(
                a => a.Action == AdminAuditEvent.ReceptionWindowUpdated), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task SetActive_TogglesAndAudits()
    {
        var db = $"rw-setactive-{Guid.NewGuid():N}";
        int windowId;
        using (var ctx = CreateContext(db))
        {
            var processId = await SeedProcessAsync(ctx);
            windowId = await NewService(ctx).CreateAsync(
                new CreateReceptionWindowCommand(processId, "W", Start, End, null, null, 0),
                Actor, CancellationToken.None);
        }

        using (var ctx = CreateContext(db))
            await NewService(ctx).SetActiveAsync(windowId, false, Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            Assert.That((await ctx.ProcessEvents.FirstAsync(e => e.Id == windowId)).IsActive, Is.False);
            Assert.That(await ctx.AdminAuditEvents.CountAsync(
                a => a.Action == AdminAuditEvent.ReceptionWindowDeactivated), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Delete_RemovesRow_AndAudits()
    {
        var db = $"rw-delete-{Guid.NewGuid():N}";
        int windowId;
        using (var ctx = CreateContext(db))
        {
            var processId = await SeedProcessAsync(ctx);
            windowId = await NewService(ctx).CreateAsync(
                new CreateReceptionWindowCommand(processId, "W", Start, End, null, null, 0),
                Actor, CancellationToken.None);
        }

        using (var ctx = CreateContext(db))
            await NewService(ctx).DeleteAsync(windowId, Actor, CancellationToken.None);

        using (var ctx = CreateContext(db))
        {
            Assert.That(await ctx.ProcessEvents.AnyAsync(e => e.Id == windowId), Is.False);
            Assert.That(await ctx.AdminAuditEvents.CountAsync(
                a => a.Action == AdminAuditEvent.ReceptionWindowDeleted), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task OverlappingWindows_AreAllowed()
    {
        // FR-003 — union semantics; no unique/overlap constraint.
        var db = $"rw-overlap-{Guid.NewGuid():N}";
        using var ctx = CreateContext(db);
        var processId = await SeedProcessAsync(ctx);
        var svc = NewService(ctx);

        await svc.CreateAsync(new CreateReceptionWindowCommand(processId, "A", Start, End, null, null, 0), Actor, CancellationToken.None);
        await svc.CreateAsync(new CreateReceptionWindowCommand(processId, "B", Start.AddDays(5), End.AddDays(5), null, null, 1), Actor, CancellationToken.None);

        Assert.That(await ctx.ProcessEvents.CountAsync(e => e.ProcessId == processId), Is.EqualTo(2));
    }
}
