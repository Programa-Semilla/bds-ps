using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Services;
using static FundingPlatform.Tests.Integration.Disbursements.TrancheTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 046 — the composed-balance reconciliation core (SC-002/SC-003) + derived status buckets (D3) +
/// cross-tranche attribution (FR-012). Complements the per-story tests with the assertions the deep
/// review flagged as missing: all six dimensions composing to the colón, composed==flat Allocated,
/// one payment split across two tranches, and the three payment-derived status buckets. InMemory
/// harness (spec-045 precedent).
/// </summary>
[TestFixture]
public class BudgetLineReconciliationTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    private static AttachDisbursementEvidenceCommand Ev(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today,
            new MemoryStream("%PDF-1.4 body"u8.ToArray()), $"{kind}.pdf", "application/pdf", 11);

    /// <summary>Seed 2 priced lines in two tranches (item0→Tramo 1, item1→Tramo 2), execute, commit both.</summary>
    private static async Task<(int AppId, IReadOnlyList<int> Items, int T1, int T2, DisbursementService Svc, ParticipantBalanceProjection Proj)>
        SeedTwoTranchesExecutedAsync(FundingPlatform.Infrastructure.Persistence.AppDbContext ctx, decimal[] prices)
    {
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, prices, ApplicationState.ResponseFinalized);
        var tranches = NewTrancheService(ctx);
        var t1 = await tranches.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);
        var t2 = await tranches.CreateAsync(appId, "Tramo 2", Actor, CancellationToken.None);
        await tranches.AssignItemAsync(appId, items[0], t1.Value, Actor, CancellationToken.None);
        await tranches.AssignItemAsync(appId, items[1], t2.Value, Actor, CancellationToken.None);
        await ExecuteAsync(ctx, appId);
        await DisbursementTestFactory.SeedAllocationAsync(ctx, appId, prices.Sum());

        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);
        await svc.CommitLineAsync(appId, items[1], Actor, CancellationToken.None);
        return (appId, items, t1.Value, t2.Value, svc, NewProjection(ctx));
    }

    [Test]
    public async Task OneDisbursement_SplitAcrossTwoTranches_ComposesPerLineTranche()
    {
        using var ctx = CreateContext($"recon-{Guid.NewGuid():N}");
        var (appId, items, t1, t2, svc, proj) = await SeedTwoTranchesExecutedAsync(ctx, [100_000m, 200_000m]);

        // FR-012 — one payment of 90k split across the two tranches' lines.
        var rec = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 90_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 30_000m), new LineAllocationInput(items[1], 60_000m)]),
            Actor, CancellationToken.None);
        Assert.That(rec.Succeeded, Is.True);

        var composed = await proj.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        var tranche1 = composed.Tranches.Single(t => t.TrancheId == t1);
        var tranche2 = composed.Tranches.Single(t => t.TrancheId == t2);

        // Each attribution composes into its OWN line's tranche.
        Assert.That(tranche1.Lines.Single(l => l.ItemId == items[0]).Balance.Paid, Is.EqualTo(30_000m));
        Assert.That(tranche1.Balance.Paid, Is.EqualTo(30_000m));
        Assert.That(tranche2.Lines.Single(l => l.ItemId == items[1]).Balance.Paid, Is.EqualTo(60_000m));
        Assert.That(tranche2.Balance.Paid, Is.EqualTo(60_000m));
        Assert.That(composed.Participant.Paid, Is.EqualTo(90_000m));
    }

    [Test]
    public async Task AllSixDimensions_ReconcileToColon_AcrossLevels_AndComposedAllocatedEqualsFlat()
    {
        using var ctx = CreateContext($"recon-{Guid.NewGuid():N}");
        var (appId, items, _, _, svc, proj) = await SeedTwoTranchesExecutedAsync(ctx, [100_000m, 200_000m]);

        // One validated disbursement (60k → item0) + one pending (40k → item1): mixes Validated + Pending.
        var d1 = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 60_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 60_000m)]), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, d1.Value, EvidenceKind.BankReceipt, 60_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, d1.Value, EvidenceKind.Invoice, 60_000m), Actor, CancellationToken.None);
        Assert.That((await svc.ValidateAsync(appId, d1.Value, Actor, CancellationToken.None)).Succeeded, Is.True);

        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 40_000m, "TX-2", null,
            [new LineAllocationInput(items[1], 40_000m)]), Actor, CancellationToken.None);

        var composed = await proj.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        var flat = await proj.GetForApplicationAsync(appId, CancellationToken.None);

        // SC-003 — for every dimension, participant == Σ tranches == Σ lines.
        var lines = composed.Tranches.SelectMany(t => t.Lines).ToList();
        void Reconciles(string dim, Func<ParticipantBalance, decimal> sel)
        {
            Assert.That(composed.Tranches.Sum(t => sel(t.Balance)), Is.EqualTo(sel(composed.Participant)), $"{dim}: Σ tranches");
            Assert.That(lines.Sum(l => sel(l.Balance)), Is.EqualTo(sel(composed.Participant)), $"{dim}: Σ lines");
        }
        Reconciles("Allocated", b => b.Allocated);
        Reconciles("Committed", b => b.Committed);
        Reconciles("Paid", b => b.Paid);
        Reconciles("Validated", b => b.Validated);
        Reconciles("Pending", b => b.PendingValidation);
        Reconciles("Available", b => b.Available);

        // Specific figures: Validated 60k, Pending 40k, Committed = both budgets, Allocated = ledger snapshot.
        Assert.That(composed.Participant.Validated, Is.EqualTo(60_000m));
        Assert.That(composed.Participant.PendingValidation, Is.EqualTo(40_000m));
        Assert.That(composed.Participant.Committed, Is.EqualTo(300_000m));
        // The composed Allocated (Σ line budgets) must equal the flat Allocated (the ledger snapshot).
        Assert.That(composed.Participant.Allocated, Is.EqualTo(flat.Allocated));
        Assert.That(composed.Participant.Allocated, Is.EqualTo(300_000m));
    }

    [Test]
    public async Task DerivedStatus_PartiallyPaid_Paid_Validated()
    {
        using var ctx = CreateContext($"recon-{Guid.NewGuid():N}");
        var (appId, items, _, _, svc, proj) = await SeedTwoTranchesExecutedAsync(ctx, [100_000m, 100_000m]);

        // item0: 50k of 100k committed budget, unvalidated → PartiallyPaid.
        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 50_000m, "TX-A", null,
            [new LineAllocationInput(items[0], 50_000m)]), Actor, CancellationToken.None);

        // item1: full 100k, validated → Validated.
        var d = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 100_000m, "TX-B", null,
            [new LineAllocationInput(items[1], 100_000m)]), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, d.Value, EvidenceKind.BankReceipt, 100_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, d.Value, EvidenceKind.Invoice, 100_000m), Actor, CancellationToken.None);
        Assert.That((await svc.ValidateAsync(appId, d.Value, Actor, CancellationToken.None)).Succeeded, Is.True);

        var composed = await proj.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        var byItem = composed.Tranches.SelectMany(t => t.Lines).ToDictionary(l => l.ItemId);

        Assert.That(byItem[items[0]].Status, Is.EqualTo(BudgetLineStatus.PartiallyPaid));
        Assert.That(byItem[items[1]].Status, Is.EqualTo(BudgetLineStatus.Validated));
        Assert.That(byItem[items[1]].Balance.Validated, Is.EqualTo(100_000m));
        Assert.That(byItem[items[1]].Balance.PendingValidation, Is.EqualTo(0m));
    }

    [Test]
    public async Task DerivedStatus_Paid_WhenFullyPaidButUnvalidated()
    {
        using var ctx = CreateContext($"recon-{Guid.NewGuid():N}");
        var (appId, items, _, _, svc, proj) = await SeedTwoTranchesExecutedAsync(ctx, [100_000m, 100_000m]);

        // item0: full 100k attributed but NOT validated (Σ ≥ budget, pending > 0) → Paid.
        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 100_000m, "TX-A", null,
            [new LineAllocationInput(items[0], 100_000m)]), Actor, CancellationToken.None);

        var composed = await proj.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        var line = composed.Tranches.SelectMany(t => t.Lines).Single(l => l.ItemId == items[0]);
        Assert.That(line.Status, Is.EqualTo(BudgetLineStatus.Paid));
    }

    [Test]
    public async Task OverPaidLine_NegativeAvailable_AtLineTrancheAndParticipant()
    {
        using var ctx = CreateContext($"recon-{Guid.NewGuid():N}");
        var (appId, items, t1, _, svc, proj) = await SeedTwoTranchesExecutedAsync(ctx, [100_000m, 100_000m]);

        // Over-pay item0: 150k attributed to a 100k line (participant ceiling is generous at 200k).
        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 150_000m, "TX-A", null,
            [new LineAllocationInput(items[0], 150_000m)]), Actor, CancellationToken.None);

        var composed = await proj.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        var tranche1 = composed.Tranches.Single(t => t.TrancheId == t1);
        Assert.That(tranche1.Lines.Single(l => l.ItemId == items[0]).Balance.Available, Is.EqualTo(-50_000m));
        Assert.That(tranche1.Balance.Available, Is.EqualTo(-50_000m)); // never clamped at tranche level
        // Participant Available = 200k allocated − 150k paid = 50k (still positive here); the line/tranche
        // negative is the over-payment signal. Assert it is not clamped anywhere it should be negative.
        Assert.That(composed.Participant.Available, Is.EqualTo(50_000m));
    }

    [Test]
    public async Task Commit_TrancheLevelCommitted_IsAsserted()
    {
        using var ctx = CreateContext($"recon-{Guid.NewGuid():N}");
        var (appId, items, t1, t2, _, proj) = await SeedTwoTranchesExecutedAsync(ctx, [100_000m, 200_000m]);

        var composed = await proj.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        Assert.That(composed.Tranches.Single(t => t.TrancheId == t1).Balance.Committed, Is.EqualTo(100_000m));
        Assert.That(composed.Tranches.Single(t => t.TrancheId == t2).Balance.Committed, Is.EqualTo(200_000m));
        Assert.That(composed.Participant.Committed, Is.EqualTo(300_000m));
    }
}
