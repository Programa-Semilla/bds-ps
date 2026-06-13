using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 021 / FR-003 / FR-004 / OQ-1 / SC-002 — invariants for the
/// <see cref="Plantilla"/> → <see cref="ProcessPlantilla"/> copy-on-assign
/// snapshot mechanic. The snapshot must be independent of post-assignment
/// edits to the base Plantilla. Spec 035 / D4 — the impact-template gating was
/// removed, so assignment no longer requires (or snapshots) impact templates.
/// </summary>
[TestFixture]
public class PlantillaSnapshotTests
{
    private static Process MakeProcess(int id = 100)
    {
        var process = Process.Create($"Process {id}", 1);
        // Process.Id has a private setter — bump it via reflection so the
        // snapshot constructor accepts processId > 0 (required by guard).
        typeof(Process).GetProperty("Id")!.SetValue(process, id);
        return process;
    }

    private static Plantilla MakePlantilla(
        string name = "Plantilla A",
        int minimumQuotationsPerItem = 3,
        long requiredFieldFlags = 0xF,
        int id = 1)
    {
        var plantilla = Plantilla.Create(name, minimumQuotationsPerItem, requiredFieldFlags);
        typeof(Plantilla).GetProperty("Id")!.SetValue(plantilla, id);
        return plantilla;
    }

    [Test]
    public void AssignTo_ReturnsSnapshotMatchingBase()
    {
        var process = MakeProcess(100);
        var plantilla = MakePlantilla(
            minimumQuotationsPerItem: 3,
            requiredFieldFlags: 0xAB,
            id: 5);

        var snapshot = plantilla.AssignTo(process);

        Assert.That(snapshot.ProcessId, Is.EqualTo(100));
        Assert.That(snapshot.SourcePlantillaId, Is.EqualTo(5));
        Assert.That(snapshot.MinimumQuotationsPerItem, Is.EqualTo(3));
        Assert.That(snapshot.RequiredFieldFlags, Is.EqualTo(0xAB));
        Assert.That(process.Plantilla, Is.SameAs(snapshot));
    }

    [Test]
    public void AssignTo_EditBasePlantilla_DoesNotMutateSnapshot()
    {
        var process = MakeProcess(100);
        var plantilla = MakePlantilla(
            minimumQuotationsPerItem: 3,
            requiredFieldFlags: 0xAB,
            id: 5);

        var snapshot = plantilla.AssignTo(process);

        // Mutate the base AFTER assignment — SC-002: snapshot stays frozen.
        plantilla.Edit("Renamed Plantilla", 7, 0xFF);

        Assert.That(snapshot.MinimumQuotationsPerItem, Is.EqualTo(3));
        Assert.That(snapshot.RequiredFieldFlags, Is.EqualTo(0xAB));
    }

    [Test]
    public void AssignTo_WithNoImpactTemplates_Succeeds()
    {
        // Spec 035 / D4 — the prior "≥ 1 impact template" guard is gone.
        var process = MakeProcess(100);
        var plantilla = MakePlantilla(id: 5);

        var snapshot = plantilla.AssignTo(process);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(process.Plantilla, Is.SameAs(snapshot));
    }

    [Test]
    public void AssignTo_WhenProcessAlreadyHasPlantilla_Throws()
    {
        var process = MakeProcess(100);
        var firstPlantilla = MakePlantilla(id: 5);

        firstPlantilla.AssignTo(process); // first assign succeeds

        var secondPlantilla = MakePlantilla(id: 6);

        Assert.Throws<InvalidOperationException>(() => secondPlantilla.AssignTo(process));
    }

    [Test]
    public void Detach_WhenAttachedToTarget_Succeeds()
    {
        var process = MakeProcess(100);
        var plantilla = MakePlantilla(id: 5);
        plantilla.AssignTo(process);

        plantilla.Detach(process, force: false, reason: null);

        Assert.That(process.Plantilla, Is.Null);
    }

    [Test]
    public void Detach_ForceWithoutReason_Throws()
    {
        var process = MakeProcess(100);
        var plantilla = MakePlantilla(id: 5);
        plantilla.AssignTo(process);

        Assert.Throws<ArgumentException>(
            () => plantilla.Detach(process, force: true, reason: "  "));
    }

    [Test]
    public void Detach_NoSnapshotAttached_Throws()
    {
        var process = MakeProcess(100);
        var plantilla = MakePlantilla(id: 5);

        Assert.Throws<InvalidOperationException>(
            () => plantilla.Detach(process, force: false, reason: null));
    }
}
