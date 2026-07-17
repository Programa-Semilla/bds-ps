using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 048 — the <see cref="Discrepancy"/> aggregate's guarded transitions: waive-blocking throws,
/// reason required, auto-resolve/auto-reopen state math, and waived-reopens-on-amount-change (FR-016).
/// </summary>
[TestFixture]
public class DiscrepancyTests
{
    private const string System = "system-sentinel";
    private const string Op = "finop-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

    private static Discrepancy NewWarning() => Discrepancy.Detect(
        1, DiscrepancyScopeType.Payment, 2, ReconciliationComparison.PossibleDuplicatePayment,
        DiscrepancySeverity.Warning, 100m, 100m, 0m, "pago", System, Now);

    private static Discrepancy NewBlocking() => Discrepancy.Detect(
        1, DiscrepancyScopeType.Payment, 2, ReconciliationComparison.DisbursementVsInvoice,
        DiscrepancySeverity.Blocking, 100m, 101m, 0m, "factura", System, Now);

    [Test]
    public void Detect_StartsOpen_WithOpenedEvent()
    {
        var d = NewBlocking();
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Open));
            Assert.That(d.Difference, Is.EqualTo(1m));
            Assert.That(d.Events, Has.Count.EqualTo(1));
            Assert.That(d.Events[0].Kind, Is.EqualTo(DiscrepancyEvent.KindOpened));
        });
    }

    [Test]
    public void Waive_OnBlocking_Throws()
    {
        var d = NewBlocking();
        Assert.Throws<InvalidOperationException>(() => d.Waive("motivo", Op, Now));
    }

    [Test]
    public void Waive_WithBlankReason_Throws()
    {
        var d = NewWarning();
        Assert.Throws<ArgumentException>(() => d.Waive("   ", Op, Now));
    }

    [Test]
    public void Waive_Warning_SetsWaivedWithReasonAndEvent()
    {
        var d = NewWarning();
        d.Waive("Aceptado por el operador.", Op, Now);
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Waived));
            Assert.That(d.WaivedReason, Is.EqualTo("Aceptado por el operador."));
            Assert.That(d.Events[^1].Kind, Is.EqualTo(DiscrepancyEvent.KindWaived));
        });
    }

    [Test]
    public void AutoResolve_FromOpen_SetsResolvedAndResolvedAt()
    {
        var d = NewBlocking();
        d.AutoResolve(System, Now);
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Resolved));
            Assert.That(d.ResolvedAt, Is.EqualTo(Now));
            Assert.That(d.Events[^1].Kind, Is.EqualTo(DiscrepancyEvent.KindResolved));
        });
    }

    [Test]
    public void AutoResolve_OnWaived_IsNoOp()
    {
        var d = NewWarning();
        d.Waive("motivo", Op, Now);
        var eventsBefore = d.Events.Count;
        d.AutoResolve(System, Now);
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Waived));
            Assert.That(d.Events, Has.Count.EqualTo(eventsBefore));
        });
    }

    [Test]
    public void AutoReopen_FromResolved_ReturnsToOpen()
    {
        var d = NewBlocking();
        d.AutoResolve(System, Now);
        d.AutoReopen(System, Now);
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Open));
            Assert.That(d.ResolvedAt, Is.Null);
            Assert.That(d.Events[^1].Kind, Is.EqualTo(DiscrepancyEvent.KindReopened));
        });
    }

    [Test]
    public void Refresh_WaivedWithAmountChange_Reopens()
    {
        var d = NewWarning();
        d.Waive("motivo", Op, Now);

        d.Refresh(expected: 200m, actual: 250m, System, Now); // amount changed
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Open));
            Assert.That(d.WaivedReason, Is.Null);
            Assert.That(d.Difference, Is.EqualTo(50m));
            Assert.That(d.Events[^1].Kind, Is.EqualTo(DiscrepancyEvent.KindReopened));
        });
    }

    [Test]
    public void Refresh_WaivedSameAmount_StaysWaived()
    {
        var d = NewWarning();
        d.Waive("motivo", Op, Now);
        var eventsBefore = d.Events.Count;

        d.Refresh(expected: 100m, actual: 100m, System, Now); // unchanged
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Waived));
            Assert.That(d.Events, Has.Count.EqualTo(eventsBefore));
        });
    }

    [Test]
    public void Assign_OnWaived_Throws()
    {
        var d = NewWarning();
        d.Waive("motivo", Op, Now);
        Assert.Throws<InvalidOperationException>(() => d.Assign(Op, Op, Now));
    }

    [Test]
    public void Assign_OnResolved_Throws()
    {
        var d = NewBlocking();
        d.AutoResolve(System, Now);
        Assert.Throws<InvalidOperationException>(() => d.Assign(Op, Op, Now));
    }

    [Test]
    public void MarkUnderCorrection_OnWaived_Throws()
    {
        var d = NewWarning();
        d.Waive("motivo", Op, Now);
        Assert.Throws<InvalidOperationException>(() => d.MarkUnderCorrection(Op, null, Now));
    }

    [Test]
    public void Refresh_Open_KeepsStateAndAssignee()
    {
        var d = NewBlocking();
        d.Assign(Op, Op, Now);
        d.Refresh(expected: 100m, actual: 150m, System, Now);
        Assert.Multiple(() =>
        {
            Assert.That(d.State, Is.EqualTo(DiscrepancyState.Assigned));
            Assert.That(d.AssigneeUserId, Is.EqualTo(Op));
            Assert.That(d.Difference, Is.EqualTo(50m));
        });
    }
}
