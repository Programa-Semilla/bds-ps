using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 021 / FR-001 / FR-006 / OQ-2 / OQ-3 — domain invariants for the
/// <see cref="Process"/> aggregate root (Active/Closed lifecycle + per-Process
/// stage-window overrides).
/// </summary>
[TestFixture]
public class ProcessTests
{
    [Test]
    public void Create_ReturnsActiveProcessWithName()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var process = Process.Create("Crocus 2025", 1);

        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.That(process.Name, Is.EqualTo("Crocus 2025"));
        Assert.That(process.Status, Is.EqualTo(ProcessStatus.Active));
        Assert.That(process.CreatedAt, Is.InRange(before, after));
        Assert.That(process.ClosedAt, Is.Null);
        // Spec 044 — SolicitudWindowDays removed.
        Assert.That(process.RevisionWindowDays, Is.Null);
        Assert.That(process.FacturacionWindowDays, Is.Null);
    }

    [Test]
    public void Create_TrimsName()
    {
        var process = Process.Create("  Crocus 2025  ", 1);

        Assert.That(process.Name, Is.EqualTo("Crocus 2025"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Create_RejectsEmptyOrWhitespaceName(string? raw)
    {
        Assert.Throws<ArgumentException>(() => Process.Create(raw!, 1));
    }

    [Test]
    public void Create_RejectsOverLength()
    {
        var name = new string('x', Process.MaxNameLength + 1);

        Assert.Throws<ArgumentException>(() => Process.Create(name, 1));
    }

    [Test]
    public void Close_FlipsStatusAndStampsClosedAt()
    {
        var process = Process.Create("Crocus 2025", 1);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        process.Close();

        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.That(process.Status, Is.EqualTo(ProcessStatus.Closed));
        Assert.That(process.ClosedAt, Is.Not.Null);
        Assert.That(process.ClosedAt!.Value, Is.InRange(before, after));
    }

    [Test]
    public void Close_WhenAlreadyClosed_Throws()
    {
        var process = Process.Create("Crocus 2025", 1);
        process.Close();

        Assert.Throws<ProcessClosedException>(() => process.Close());
    }

    [Test]
    public void OverrideStageWindow_Solicitud_IsNoLongerOverridable()
    {
        // Spec 044 — the Solicitud duration window was removed; reception windows
        // gate submission timing. Solicitud is rejected by the stage switch.
        var process = Process.Create("Crocus 2025", 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => process.OverrideStageWindow(StageKind.Solicitud, 14));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => process.OverrideForStage(StageKind.Solicitud));
    }

    [Test]
    public void OverrideStageWindow_Revision_SetsField()
    {
        var process = Process.Create("Crocus 2025", 1);

        process.OverrideStageWindow(StageKind.Revision, 30);

        Assert.That(process.RevisionWindowDays, Is.EqualTo(30));
        Assert.That(process.OverrideForStage(StageKind.Revision), Is.EqualTo(30));
    }

    [Test]
    public void OverrideStageWindow_Facturacion_SetsField()
    {
        var process = Process.Create("Crocus 2025", 1);

        process.OverrideStageWindow(StageKind.Facturacion, 45);

        Assert.That(process.FacturacionWindowDays, Is.EqualTo(45));
        Assert.That(process.OverrideForStage(StageKind.Facturacion), Is.EqualTo(45));
    }

    [Test]
    public void OverrideStageWindow_Null_RevertsToDefault()
    {
        var process = Process.Create("Crocus 2025", 1);
        process.OverrideStageWindow(StageKind.Revision, 14);

        process.OverrideStageWindow(StageKind.Revision, null);

        Assert.That(process.RevisionWindowDays, Is.Null);
        Assert.That(process.OverrideForStage(StageKind.Revision), Is.Null);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-365)]
    public void OverrideStageWindow_RejectsNonPositiveDays(int days)
    {
        var process = Process.Create("Crocus 2025", 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => process.OverrideStageWindow(StageKind.Revision, days));
    }

    [Test]
    public void OverrideStageWindow_WhenClosed_Throws()
    {
        var process = Process.Create("Crocus 2025", 1);
        process.Close();

        Assert.Throws<ProcessClosedException>(
            () => process.OverrideStageWindow(StageKind.Revision, 14));
    }

    [Test]
    public void OverrideForStage_NoOverrideSet_ReturnsNull()
    {
        var process = Process.Create("Crocus 2025", 1);

        Assert.That(process.OverrideForStage(StageKind.Revision), Is.Null);
        Assert.That(process.OverrideForStage(StageKind.Facturacion), Is.Null);
    }

    [Test]
    public void Rename_TrimsAndUpdates()
    {
        var process = Process.Create("Crocus 2025", 1);

        process.Rename("  Crocus 2026  ");

        Assert.That(process.Name, Is.EqualTo("Crocus 2026"));
    }

    // ----- Spec 030 / FR-004 / FR-006 — rename boundary cases. -----

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Rename_RejectsEmptyOrWhitespace(string? raw)
    {
        var process = Process.Create("Crocus 2025", 1);

        Assert.Throws<ArgumentException>(() => process.Rename(raw!));
        Assert.That(process.Name, Is.EqualTo("Crocus 2025"), "Name must be unchanged on rejection.");
    }

    [Test]
    public void Rename_AcceptsMaxLength()
    {
        var process = Process.Create("Crocus 2025", 1);
        var name = new string('x', Process.MaxNameLength);

        process.Rename(name);

        Assert.That(process.Name, Is.EqualTo(name));
    }

    [Test]
    public void Rename_RejectsOverLength()
    {
        var process = Process.Create("Crocus 2025", 1);
        var name = new string('x', Process.MaxNameLength + 1);

        Assert.Throws<ArgumentException>(() => process.Rename(name));
        Assert.That(process.Name, Is.EqualTo("Crocus 2025"), "Name must be unchanged on rejection.");
    }

    [Test]
    public void Rename_EqualName_IsNoOp()
    {
        var process = Process.Create("Crocus 2025", 1);

        process.Rename("Crocus 2025");

        Assert.That(process.Name, Is.EqualTo("Crocus 2025"));
    }

    [Test]
    public void Rename_TrimmedEqualName_IsNoOp()
    {
        var process = Process.Create("Crocus 2025", 1);

        // After trim this equals the current name → no-op (FR-006 / SC-005).
        process.Rename("  Crocus 2025  ");

        Assert.That(process.Name, Is.EqualTo("Crocus 2025"));
    }
}
