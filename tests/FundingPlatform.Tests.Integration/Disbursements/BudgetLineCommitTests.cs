using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static FundingPlatform.Tests.Integration.Disbursements.TrancheTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 046 / US2 — Financial Operator commits/un-commits budget-lines. Covers the Committed
/// dimension in the flat + composed projections, the un-commit-with-payment refusal (FR-007), and
/// a CommitState round-trip. InMemory harness (spec-045 precedent); real-SQL TINYINT materialization
/// is proven by the E2E suite.
/// </summary>
[TestFixture]
public class BudgetLineCommitTests
{
    [Test]
    public async Task Commit_RaisesCommittedDimension_AtAllLevels()
    {
        using var ctx = CreateContext($"commit-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        var projection = NewProjection(ctx);

        var r = await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);
        Assert.That(r.Succeeded, Is.True);

        // Flat balance: Committed = Σ committed line budgets = 100k.
        var flat = await projection.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.That(flat.Committed, Is.EqualTo(100_000m));

        // Composed: the committed line shows Committed = its budget; the other shows 0.
        var composed = await projection.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        Assert.That(composed.Participant.Committed, Is.EqualTo(100_000m));
        var lines = composed.Tranches.SelectMany(t => t.Lines).ToDictionary(l => l.ItemId);
        Assert.That(lines[items[0]].Balance.Committed, Is.EqualTo(100_000m));
        Assert.That(lines[items[0]].Status, Is.EqualTo(BudgetLineStatus.Committed));
        Assert.That(lines[items[1]].Balance.Committed, Is.EqualTo(0m));
        Assert.That(lines[items[1]].Status, Is.EqualTo(BudgetLineStatus.Uncommitted));
    }

    [Test]
    public async Task Commit_IsIdempotent_AndUncommitReverses()
    {
        using var ctx = CreateContext($"commit-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());

        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None); // idempotent
        var committed = await ctx.Items.AsNoTracking().SingleAsync(i => i.Id == items[0]);
        Assert.That(committed.CommitState, Is.EqualTo(ItemCommitState.Committed));

        await svc.UncommitLineAsync(appId, items[0], Actor, CancellationToken.None);
        var uncommitted = await ctx.Items.AsNoTracking().SingleAsync(i => i.Id == items[0]);
        Assert.That(uncommitted.CommitState, Is.EqualTo(ItemCommitState.Uncommitted));
    }

    [Test]
    public async Task Uncommit_RefusedWhenLineHasAttributedPayment()
    {
        using var ctx = CreateContext($"commit-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);

        // Seed a non-cancelled disbursement + a line allocation attributing part of it to the line.
        var app = await ctx.Applications.SingleAsync(a => a.Id == appId);
        var disb = Disbursement.Record(app, Actor, new DateOnly(2026, 7, 15), 50_000m, "TX-1", null);
        ctx.Disbursements.Add(disb);
        await ctx.SaveChangesAsync();
        ctx.DisbursementLineAllocations.Add(DisbursementLineAllocation.For(disb.Id, items[0], 50_000m));
        await ctx.SaveChangesAsync();

        var result = await svc.UncommitLineAsync(appId, items[0], Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.LineHasPayment));
    }

    [Test]
    public async Task Commit_RefusedOnNonExecutedApplication()
    {
        using var ctx = CreateContext($"commit-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m], ApplicationState.ResponseFinalized);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());

        var result = await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.NotExecuted));
    }
}
