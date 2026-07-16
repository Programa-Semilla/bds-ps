using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static FundingPlatform.Tests.Integration.Disbursements.TrancheTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 046 / US3 — per-line attribution: Record/Edit persist and replace the split, mismatched
/// splits and uncommitted targets are rejected, Validar re-checks per-line over-payment against
/// fresh sums, and the composed projection composes per-line Paid/Validated/Pending (SC-002/003/004).
/// InMemory harness (spec-045 precedent); real-SQL is proven by the E2E suite.
/// </summary>
[TestFixture]
public class LineAttributionTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    private static AttachDisbursementEvidenceCommand Ev(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today,
            new MemoryStream("%PDF-1.4 body"u8.ToArray()), $"{kind}.pdf", "application/pdf", 11);

    [Test]
    public async Task Record_WithValidSplit_PersistsAllocations_AndComposesPerLine()
    {
        using var ctx = CreateContext($"attr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        var projection = NewProjection(ctx);

        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);
        await svc.CommitLineAsync(appId, items[1], Actor, CancellationToken.None);

        var cmd = new RecordDisbursementCommand(appId, Today, 90_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 50_000m), new LineAllocationInput(items[1], 40_000m)]);
        var rec = await svc.RecordAsync(cmd, Actor, CancellationToken.None);
        Assert.That(rec.Succeeded, Is.True);

        var allocations = await ctx.DisbursementLineAllocations.AsNoTracking().ToListAsync();
        Assert.That(allocations, Has.Count.EqualTo(2));

        // Per-line composition: pending (recorded, not yet validated) reflects the attributions.
        var composed = await projection.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        var lines = composed.Tranches.SelectMany(t => t.Lines).ToDictionary(l => l.ItemId);
        Assert.That(lines[items[0]].Balance.Paid, Is.EqualTo(50_000m));
        Assert.That(lines[items[0]].Balance.PendingValidation, Is.EqualTo(50_000m));
        Assert.That(lines[items[1]].Balance.Paid, Is.EqualTo(40_000m));
    }

    [Test]
    public async Task Record_SplitMismatch_Rejected()
    {
        using var ctx = CreateContext($"attr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);

        var cmd = new RecordDisbursementCommand(appId, Today, 90_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 80_000m)]); // 80k ≠ 90k
        var rec = await svc.RecordAsync(cmd, Actor, CancellationToken.None);

        Assert.That(rec.Succeeded, Is.False);
        Assert.That(rec.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.SplitMismatch));
        Assert.That(await ctx.Disbursements.AnyAsync(), Is.False, "nothing persisted on rejection");
    }

    [Test]
    public async Task Record_AttributionToUncommittedLine_Rejected()
    {
        using var ctx = CreateContext($"attr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        // items[0] NOT committed.

        var cmd = new RecordDisbursementCommand(appId, Today, 50_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 50_000m)]);
        var rec = await svc.RecordAsync(cmd, Actor, CancellationToken.None);

        Assert.That(rec.Succeeded, Is.False);
        Assert.That(rec.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.LineNotCommitted));
    }

    [Test]
    public async Task Validate_OverpaysLine_Blocked_ReCheckedAgainstFreshSums()
    {
        using var ctx = CreateContext($"attr-{Guid.NewGuid():N}");
        // Line budget 100k. Two disbursements each attribute 60k → 120k > 100k committed budget.
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m], ApplicationState.AgreementExecuted);
        await DisbursementTestFactory.SeedAllocationAsync(ctx, appId, 1_000_000m); // generous participant ceiling
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);

        async Task<int> RecordAndProveAsync(decimal amount, string txn)
        {
            var r = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, amount, txn, null,
                [new LineAllocationInput(items[0], amount)]), Actor, CancellationToken.None);
            Assert.That(r.Succeeded, Is.True);
            await svc.AttachEvidenceAsync(Ev(appId, r.Value, EvidenceKind.BankReceipt, amount), Actor, CancellationToken.None);
            await svc.AttachEvidenceAsync(Ev(appId, r.Value, EvidenceKind.Invoice, amount), Actor, CancellationToken.None);
            return r.Value;
        }

        // Record + validate d1 (60k ≤ 100k committed budget) — clean.
        var d1 = await RecordAndProveAsync(60_000m, "TX-1");
        var v1 = await svc.ValidateAsync(appId, d1, Actor, CancellationToken.None);
        Assert.That(v1.Succeeded, Is.True);

        // Record + prove d2 (another 60k to the same line). Validar re-reads the fresh per-line sum
        // (60k validated + 60k pending = 120k > 100k) and blocks — the race-proof gate.
        var d2 = await RecordAndProveAsync(60_000m, "TX-2");
        var v2 = await svc.ValidateAsync(appId, d2, Actor, CancellationToken.None);
        Assert.That(v2.Succeeded, Is.False);
        Assert.That(v2.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.LineOverpayment));
    }

    [Test]
    public async Task Edit_ReplacesSplit()
    {
        using var ctx = CreateContext($"attr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);
        await svc.CommitLineAsync(appId, items[1], Actor, CancellationToken.None);

        var rec = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 90_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 90_000m)]), Actor, CancellationToken.None);

        // Edit re-splits across both lines.
        var edit = await svc.EditAsync(new EditDisbursementCommand(appId, rec.Value, Today, 90_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 50_000m), new LineAllocationInput(items[1], 40_000m)]), Actor, CancellationToken.None);
        Assert.That(edit.Succeeded, Is.True);

        var allocations = await ctx.DisbursementLineAllocations.AsNoTracking()
            .Where(a => a.DisbursementId == rec.Value).ToListAsync();
        Assert.That(allocations, Has.Count.EqualTo(2));
        Assert.That(allocations.Sum(a => a.Amount), Is.EqualTo(90_000m));
    }
}
