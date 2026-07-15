using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Processes;

/// <summary>
/// Spec 044 / US5 (T037) — proves the general <c>ProcessEvent</c> shape admits
/// future event types without a reshape: a reception window persists with
/// <c>EventType=ReceptionWindow</c> + <c>ControlsSubmissionAvailability=true</c>,
/// and an <c>Informational</c> row round-trips (schema-only, no behavior). The
/// real-SQL TINYINT <c>HasConversion&lt;byte&gt;</c> materialization is exercised by
/// the E2E suite; this asserts the model round-trips.
/// </summary>
[TestFixture]
public class ProcessEventSchemaTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task ReceptionWindow_PersistsWithBehaviorFlags()
    {
        var db = $"pe-reception-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
        {
            var fund = Fund.Create("F", "d");
            ctx.Funds.Add(fund);
            await ctx.SaveChangesAsync();
            var process = Process.Create("P", fund.Id);
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();

            var w = ProcessEvent.CreateReceptionWindow(process.Id, "W", Start, End, null, null, 0, "admin");
            ctx.ProcessEvents.Add(w);
            await ctx.SaveChangesAsync();
            id = w.Id;
        }

        using (var ctx = CreateContext(db))
        {
            var w = await ctx.ProcessEvents.FirstAsync(e => e.Id == id);
            Assert.That(w.EventType, Is.EqualTo(ProcessEventType.ReceptionWindow));
            Assert.That(w.ControlsSubmissionAvailability, Is.True);
        }
    }

    [Test]
    public async Task NonReceptionEventType_RoundTrips_NoBehavior()
    {
        var db = $"pe-informational-{Guid.NewGuid():N}";
        int id;
        using (var ctx = CreateContext(db))
        {
            var fund = Fund.Create("F", "d");
            ctx.Funds.Add(fund);
            await ctx.SaveChangesAsync();
            var process = Process.Create("P", fund.Id);
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();

            // The reception-window factory is the only public constructor; force a
            // reserved event type via the EF entry to prove the schema admits it.
            var e = ProcessEvent.CreateReceptionWindow(process.Id, "Aviso", Start, End, null, null, 0, "admin");
            ctx.ProcessEvents.Add(e);
            await ctx.SaveChangesAsync();
            ctx.Entry(e).Property(nameof(ProcessEvent.EventType)).CurrentValue = ProcessEventType.Informational;
            ctx.Entry(e).Property(nameof(ProcessEvent.ControlsSubmissionAvailability)).CurrentValue = false;
            await ctx.SaveChangesAsync();
            id = e.Id;
        }

        using (var ctx = CreateContext(db))
        {
            var e = await ctx.ProcessEvents.FirstAsync(x => x.Id == id);
            Assert.That(e.EventType, Is.EqualTo(ProcessEventType.Informational));
            Assert.That(e.ControlsSubmissionAvailability, Is.False);
        }
    }
}
