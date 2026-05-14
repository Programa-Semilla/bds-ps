using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 021 / FR-003 / FR-004 / OQ-1 / SC-002 — invariants for the
/// <see cref="Plantilla"/> → <see cref="ProcessPlantilla"/> copy-on-assign
/// snapshot mechanic. The snapshot must be independent of post-assignment
/// edits to the base Plantilla.
/// </summary>
[TestFixture]
public class PlantillaSnapshotTests
{
    private static Process MakeProcess(int id = 100)
    {
        var process = Process.Create($"Process {id}");
        // Process.Id has a private setter — bump it via reflection so the
        // snapshot constructor accepts processId > 0 (required by guard).
        typeof(Process).GetProperty("Id")!.SetValue(process, id);
        return process;
    }

    private static ImpactTemplate MakeImpactTemplate(int id, string name)
    {
        var template = new ImpactTemplate(name, description: null, isActive: true);
        typeof(ImpactTemplate).GetProperty("Id")!.SetValue(template, id);
        return template;
    }

    private static Plantilla MakePlantilla(
        string name = "Plantilla A",
        int minimumQuotationsPerItem = 3,
        long requiredFieldFlags = 0xF,
        int id = 1,
        IEnumerable<ImpactTemplate>? templates = null)
    {
        var plantilla = Plantilla.Create(name, minimumQuotationsPerItem, requiredFieldFlags);
        typeof(Plantilla).GetProperty("Id")!.SetValue(plantilla, id);

        foreach (var t in templates ?? new[] { MakeImpactTemplate(10, "ImpactA") })
        {
            plantilla.AttachImpactTemplate(t);
        }
        return plantilla;
    }

    [Test]
    public void AssignTo_ReturnsSnapshotMatchingBase()
    {
        var process = MakeProcess(100);
        var template = MakeImpactTemplate(10, "ImpactA");
        var plantilla = MakePlantilla(
            minimumQuotationsPerItem: 3,
            requiredFieldFlags: 0xAB,
            id: 5,
            templates: new[] { template });

        var snapshot = plantilla.AssignTo(process);

        Assert.That(snapshot.ProcessId, Is.EqualTo(100));
        Assert.That(snapshot.SourcePlantillaId, Is.EqualTo(5));
        Assert.That(snapshot.MinimumQuotationsPerItem, Is.EqualTo(3));
        Assert.That(snapshot.RequiredFieldFlags, Is.EqualTo(0xAB));
        Assert.That(snapshot.ImpactTemplateIdsCsv, Is.EqualTo("10"));
        Assert.That(process.Plantilla, Is.SameAs(snapshot));
    }

    [Test]
    public void AssignTo_EditBasePlantilla_DoesNotMutateSnapshot()
    {
        var process = MakeProcess(100);
        var template = MakeImpactTemplate(10, "ImpactA");
        var plantilla = MakePlantilla(
            minimumQuotationsPerItem: 3,
            requiredFieldFlags: 0xAB,
            id: 5,
            templates: new[] { template });

        var snapshot = plantilla.AssignTo(process);

        // Mutate the base AFTER assignment — SC-002: snapshot stays frozen.
        plantilla.Edit("Renamed Plantilla", 7, 0xFF);
        var freshTemplate = MakeImpactTemplate(11, "ImpactB");
        plantilla.AttachImpactTemplate(freshTemplate);

        Assert.That(snapshot.MinimumQuotationsPerItem, Is.EqualTo(3));
        Assert.That(snapshot.RequiredFieldFlags, Is.EqualTo(0xAB));
        Assert.That(snapshot.ImpactTemplateIdsCsv, Is.EqualTo("10"));
    }

    [Test]
    public void AssignTo_WithZeroImpactTemplates_Throws()
    {
        var process = MakeProcess(100);
        var plantilla = Plantilla.Create("Empty", minimumQuotationsPerItem: 3, requiredFieldFlags: 0);
        typeof(Plantilla).GetProperty("Id")!.SetValue(plantilla, 5);

        Assert.Throws<InvalidOperationException>(() => plantilla.AssignTo(process));
    }

    [Test]
    public void AssignTo_WhenProcessAlreadyHasPlantilla_Throws()
    {
        var process = MakeProcess(100);
        var template = MakeImpactTemplate(10, "ImpactA");
        var firstPlantilla = MakePlantilla(id: 5, templates: new[] { template });

        firstPlantilla.AssignTo(process); // first assign succeeds

        var secondPlantilla = MakePlantilla(id: 6, templates: new[] { MakeImpactTemplate(11, "ImpactB") });

        Assert.Throws<InvalidOperationException>(() => secondPlantilla.AssignTo(process));
    }

    [Test]
    public void Detach_WhenAttachedToTarget_Succeeds()
    {
        var process = MakeProcess(100);
        var template = MakeImpactTemplate(10, "ImpactA");
        var plantilla = MakePlantilla(id: 5, templates: new[] { template });
        plantilla.AssignTo(process);

        plantilla.Detach(process, force: false, reason: null);

        Assert.That(process.Plantilla, Is.Null);
    }

    [Test]
    public void Detach_ForceWithoutReason_Throws()
    {
        var process = MakeProcess(100);
        var template = MakeImpactTemplate(10, "ImpactA");
        var plantilla = MakePlantilla(id: 5, templates: new[] { template });
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

    [Test]
    public void ImpactTemplateIds_RoundTripsCsv()
    {
        var process = MakeProcess(100);
        var t1 = MakeImpactTemplate(10, "ImpactA");
        var t2 = MakeImpactTemplate(20, "ImpactB");
        var t3 = MakeImpactTemplate(30, "ImpactC");
        var plantilla = MakePlantilla(id: 5, templates: new[] { t1, t2, t3 });

        var snapshot = plantilla.AssignTo(process);
        var ids = snapshot.ImpactTemplateIds();

        Assert.That(ids, Is.EquivalentTo(new[] { 10, 20, 30 }));
    }
}
