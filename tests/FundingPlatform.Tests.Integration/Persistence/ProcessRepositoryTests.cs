// Spec 021 — see specs/021-feedback-session-may13/tasks.md T074 and SC-002.

using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 021 / US1 / T074 — EF Core round-trip tests for the new Process /
/// Plantilla / ProcessPlantilla aggregates. Covers:
///   - Create Process → loadable from DB
///   - Close() flips Status + ClosedAt
///   - OverrideStageWindow persists
///   - Plantilla.AssignTo creates ProcessPlantilla → editing the base Plantilla
///     does NOT mutate the snapshot (SC-002 invariant under real EF).
/// Uses the InMemory provider following the existing
/// <see cref="SupplierRepositoryTests"/> pattern. The cascading-uniqueness /
/// CHECK-constraint invariants live at the SQL layer (dbo.Processes.sql) and
/// are exercised by the dacpac deployment; this fixture asserts the EF mapping
/// and behavior round-trip.
/// </summary>
[TestFixture]
public class ProcessRepositoryTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task CreateProcess_RoundTrips_LoadsByIdWithDefaults()
    {
        var dbName = $"proc-create-{Guid.NewGuid():N}";

        int id;
        using (var ctx = CreateContext(dbName))
        {
            var process = Process.Create("Crocus 2025", 1);
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();
            id = process.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Processes.FirstOrDefaultAsync(p => p.Id == id);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Name, Is.EqualTo("Crocus 2025"));
            Assert.That(loaded.Status, Is.EqualTo(ProcessStatus.Active));
            Assert.That(loaded.ClosedAt, Is.Null);
            // Spec 044 — SolicitudWindowDays removed.
            Assert.That(loaded.RevisionWindowDays, Is.Null);
            Assert.That(loaded.FacturacionWindowDays, Is.Null);
        }
    }

    [Test]
    public async Task CloseProcess_PersistsStatusAndClosedAt()
    {
        var dbName = $"proc-close-{Guid.NewGuid():N}";

        int id;
        using (var ctx = CreateContext(dbName))
        {
            var process = Process.Create("Nexo 2026", 1);
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();
            id = process.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var process = await ctx.Processes.FirstAsync(p => p.Id == id);
            process.Close();
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Processes.FirstAsync(p => p.Id == id);
            Assert.That(loaded.Status, Is.EqualTo(ProcessStatus.Closed));
            Assert.That(loaded.ClosedAt, Is.Not.Null);
            Assert.That(loaded.ClosedAt!.Value, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow));
        }
    }

    [Test]
    public async Task OverrideStageWindow_PersistsPerStage()
    {
        var dbName = $"proc-stage-{Guid.NewGuid():N}";

        int id;
        using (var ctx = CreateContext(dbName))
        {
            var process = Process.Create("Crocus Override", 1);
            ctx.Processes.Add(process);
            await ctx.SaveChangesAsync();
            id = process.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var process = await ctx.Processes.FirstAsync(p => p.Id == id);
            process.OverrideStageWindow(StageKind.Facturacion, 45);
            process.OverrideStageWindow(StageKind.Revision, 21);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Processes.FirstAsync(p => p.Id == id);
            Assert.That(loaded.FacturacionWindowDays, Is.EqualTo(45));
            Assert.That(loaded.RevisionWindowDays, Is.EqualTo(21));
        }

        // Round-trip clearing one override back to null.
        using (var ctx = CreateContext(dbName))
        {
            var process = await ctx.Processes.FirstAsync(p => p.Id == id);
            process.OverrideStageWindow(StageKind.Revision, null);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Processes.FirstAsync(p => p.Id == id);
            Assert.That(loaded.RevisionWindowDays, Is.Null);
            Assert.That(loaded.FacturacionWindowDays, Is.EqualTo(45),
                "Clearing Revisión must not affect Facturación.");
        }
    }

    [Test]
    public async Task PlantillaAssignTo_PersistsSnapshot_BaseEditDoesNotMutateIt_SC002()
    {
        var dbName = $"plantilla-snapshot-{Guid.NewGuid():N}";

        int processId, plantillaId, snapshotId;
        using (var ctx = CreateContext(dbName))
        {
            var process = Process.Create("Crocus Snap", 1);
            ctx.Processes.Add(process);

            var plantilla = Plantilla.Create("PlantillaMVP-v1", minimumQuotationsPerItem: 3, requiredFieldFlags: 0xF);
            ctx.Plantillas.Add(plantilla);

            await ctx.SaveChangesAsync();

            // Spec 035 / D4 — assignment no longer requires impact templates.
            var freshPlantilla = await ctx.Plantillas
                .FirstAsync(p => p.Id == plantilla.Id);

            var freshProcess = await ctx.Processes
                .Include(p => p.Plantilla)
                .FirstAsync(p => p.Id == process.Id);

            var snapshot = freshPlantilla.AssignTo(freshProcess);
            ctx.ProcessPlantillas.Add(snapshot);
            await ctx.SaveChangesAsync();

            processId = freshProcess.Id;
            plantillaId = freshPlantilla.Id;
            snapshotId = snapshot.Id;
        }

        // Mutate the base AFTER assignment — SC-002: the snapshot's values must
        // remain frozen at the assignment-time payload.
        using (var ctx = CreateContext(dbName))
        {
            var plantilla = await ctx.Plantillas.FirstAsync(p => p.Id == plantillaId);
            plantilla.Edit("PlantillaMVP-v2-RENAMED", minimumQuotationsPerItem: 7, requiredFieldFlags: 0xFF);
            await ctx.SaveChangesAsync();
        }

        // Reload the snapshot in a brand-new context and assert independence.
        using (var ctx = CreateContext(dbName))
        {
            var snapshot = await ctx.ProcessPlantillas.FirstAsync(pp => pp.Id == snapshotId);
            Assert.That(snapshot.ProcessId, Is.EqualTo(processId));
            Assert.That(snapshot.SourcePlantillaId, Is.EqualTo(plantillaId));
            Assert.That(snapshot.MinimumQuotationsPerItem, Is.EqualTo(3),
                "Snapshot MinimumQuotationsPerItem must NOT reflect the base edit (SC-002).");
            Assert.That(snapshot.RequiredFieldFlags, Is.EqualTo(0xF),
                "Snapshot RequiredFieldFlags must NOT reflect the base edit (SC-002).");

            // The base Plantilla DID change.
            var basePlantilla = await ctx.Plantillas.FirstAsync(p => p.Id == plantillaId);
            Assert.That(basePlantilla.Name, Is.EqualTo("PlantillaMVP-v2-RENAMED"));
            Assert.That(basePlantilla.MinimumQuotationsPerItem, Is.EqualTo(7));
        }
    }
}
