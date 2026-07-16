using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;
using static FundingPlatform.Tests.Integration.Disbursements.DisbursementTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 045 / T036 — the five-dimension balance projection math: <c>Paid = Validated +
/// Pending</c>, <c>Available = Allocated − Paid</c> (negative allowed, never clamped), and a
/// validated disbursement is not double-counted.
/// </summary>
[TestFixture]
public class DisbursementProjectionTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    [Test]
    public async Task Paid_Equals_ValidatedPlusPending_AndValidatedNotDoubleCounted()
    {
        var db = $"disb-proj-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);
        var proj = NewProjection(ctx);

        // Record ₡300,000 → pending only.
        var rec = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 300_000m, "TX-1", null), Actor, CancellationToken.None);
        var b1 = await proj.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(b1.Allocated, Is.EqualTo(1_000_000m));
            Assert.That(b1.PendingValidation, Is.EqualTo(300_000m));
            Assert.That(b1.Validated, Is.EqualTo(0m));
            Assert.That(b1.Paid, Is.EqualTo(300_000m));
            Assert.That(b1.Available, Is.EqualTo(700_000m));
        });

        // Prove + validate it → moves from Pending to Validated; Paid/Available unchanged, no double-count.
        await svc.AttachEvidenceAsync(EvidenceCmd(appId, rec.Value, EvidenceKind.BankReceipt, 300_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(EvidenceCmd(appId, rec.Value, EvidenceKind.Invoice, 300_000m), Actor, CancellationToken.None);
        var val = await svc.ValidateAsync(appId, rec.Value, Actor, CancellationToken.None);
        Assert.That(val.Succeeded, Is.True);

        var b2 = await proj.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(b2.Validated, Is.EqualTo(300_000m));
            Assert.That(b2.PendingValidation, Is.EqualTo(0m), "validated disbursement must leave the Pending sum");
            Assert.That(b2.Paid, Is.EqualTo(300_000m), "Paid = Validated + Pending, no double-count");
            Assert.That(b2.Available, Is.EqualTo(700_000m));
        });
    }

    [Test]
    public async Task Available_GoesNegative_OnOverDisbursement_NeverClamped()
    {
        var db = $"disb-proj-neg-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);
        var proj = NewProjection(ctx);

        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 600_000m, "TX-1", null), Actor, CancellationToken.None);
        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 500_000m, "TX-2", null), Actor, CancellationToken.None);

        var b = await proj.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.That(b.Paid, Is.EqualTo(1_100_000m));
        Assert.That(b.Available, Is.EqualTo(-100_000m));
    }

    [Test]
    public async Task Cancelled_ContributesNothing_AndLeavesNoLedgerEntry()
    {
        var db = $"disb-proj-cancel-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);
        var proj = NewProjection(ctx);

        var keep = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 300_000m, "TX-1", null), Actor, CancellationToken.None);
        var drop = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 200_000m, "TX-2", null), Actor, CancellationToken.None);

        var cancelled = await svc.CancelAsync(appId, drop.Value, Actor, CancellationToken.None);
        Assert.That(cancelled.Succeeded, Is.True);

        var b = await proj.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            // Only the surviving ₡300,000 counts; the cancelled ₡200,000 vanishes from Paid/Pending.
            Assert.That(b.PendingValidation, Is.EqualTo(300_000m));
            Assert.That(b.Paid, Is.EqualTo(300_000m));
            Assert.That(b.Available, Is.EqualTo(700_000m));
        });

        // A cancelled disbursement never posted a ledger entry.
        var ledgerForCancelled = await ctx.DisbursementLedgerEntries
            .CountAsync(l => l.DisbursementId == drop.Value);
        Assert.That(ledgerForCancelled, Is.EqualTo(0));
    }

    private static AttachDisbursementEvidenceCommand EvidenceCmd(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today, Pdf(), $"{kind}.pdf", "application/pdf", 11);
}
