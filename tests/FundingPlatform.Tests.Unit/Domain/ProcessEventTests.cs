using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 044 — invariants + state computation for the <see cref="ProcessEvent"/>
/// aggregate (reception-window type).
/// </summary>
[TestFixture]
public class ProcessEventTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    private static ProcessEvent NewWindow()
        => ProcessEvent.CreateReceptionWindow(
            processId: 1, name: "Recepción 2026", startUtc: Start, endUtc: End,
            applicantFacingMessage: null, description: null, displayOrder: 0, createdByUserId: "admin");

    [Test]
    public void CreateReceptionWindow_SetsBehaviorFlagsAndActive()
    {
        var w = NewWindow();

        Assert.That(w.EventType, Is.EqualTo(ProcessEventType.ReceptionWindow));
        Assert.That(w.ControlsSubmissionAvailability, Is.True);
        Assert.That(w.IsActive, Is.True);
        Assert.That(w.Name, Is.EqualTo("Recepción 2026"));
    }

    [Test]
    public void CreateReceptionWindow_TrimsName()
    {
        var w = ProcessEvent.CreateReceptionWindow(1, "  Recepción  ", Start, End, null, null, 0, null);
        Assert.That(w.Name, Is.EqualTo("Recepción"));
    }

    [Test]
    public void CreateReceptionWindow_EndNotAfterStart_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ProcessEvent.CreateReceptionWindow(1, "W", End, Start, null, null, 0, null));
        Assert.Throws<ArgumentException>(() =>
            ProcessEvent.CreateReceptionWindow(1, "W", Start, Start, null, null, 0, null));
    }

    [Test]
    public void CreateReceptionWindow_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ProcessEvent.CreateReceptionWindow(1, "   ", Start, End, null, null, 0, null));
    }

    [Test]
    public void Update_RevalidatesRange()
    {
        var w = NewWindow();
        Assert.Throws<ArgumentException>(() =>
            w.Update("W", End, Start, null, null, 0, "admin"));
    }

    [Test]
    public void DeactivateThenActivate_TogglesIsActive()
    {
        var w = NewWindow();
        w.Deactivate("admin");
        Assert.That(w.IsActive, Is.False);
        w.Activate("admin");
        Assert.That(w.IsActive, Is.True);
    }

    [Test]
    public void ComputeState_ReflectsInstant()
    {
        var w = NewWindow();
        Assert.That(w.ComputeState(Start.AddDays(-1)), Is.EqualTo(ReceptionWindowState.Upcoming));
        Assert.That(w.ComputeState(Start), Is.EqualTo(ReceptionWindowState.OpenNow));
        Assert.That(w.ComputeState(End.AddDays(-1)), Is.EqualTo(ReceptionWindowState.OpenNow));
        Assert.That(w.ComputeState(End), Is.EqualTo(ReceptionWindowState.Closed));
    }
}
