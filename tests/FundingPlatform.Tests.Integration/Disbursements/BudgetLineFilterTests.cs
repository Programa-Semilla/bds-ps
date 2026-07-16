using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static FundingPlatform.Tests.Integration.Disbursements.TrancheTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 046 / US4 — filtering budget-lines on the composed projection by tranche, status, supplier,
/// and validation state (FR-020, SC-005). InMemory harness (spec-045 precedent).
/// </summary>
[TestFixture]
public class BudgetLineFilterTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    [Test]
    public async Task FilterByTranche_NarrowsToTrancheLines_SyntheticSeparate()
    {
        using var ctx = CreateContext($"filter-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m, 300_000m], ApplicationState.ResponseFinalized);
        var tranches = NewTrancheService(ctx);
        var projection = NewProjection(ctx);
        var t1 = await tranches.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);
        await tranches.AssignItemAsync(appId, items[0], t1.Value, Actor, CancellationToken.None);
        // items[1], items[2] → synthetic

        var byTranche = await projection.GetComposedForApplicationAsync(
            appId, new BudgetLineFilter(TrancheId: t1.Value), CancellationToken.None);
        Assert.That(byTranche.Tranches.SelectMany(t => t.Lines).Select(l => l.ItemId), Is.EqualTo(new[] { items[0] }));

        var bySynthetic = await projection.GetComposedForApplicationAsync(
            appId, new BudgetLineFilter(IncludeSyntheticTranche: true), CancellationToken.None);
        Assert.That(bySynthetic.Tranches.SelectMany(t => t.Lines).Select(l => l.ItemId),
            Is.EquivalentTo(new[] { items[1], items[2] }));
    }

    [Test]
    public async Task FilterByStatus_UncommittedVsCommitted()
    {
        using var ctx = CreateContext($"filter-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        var projection = NewProjection(ctx);
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);

        var committed = await projection.GetComposedForApplicationAsync(
            appId, new BudgetLineFilter(Status: BudgetLineStatus.Committed), CancellationToken.None);
        Assert.That(committed.Tranches.SelectMany(t => t.Lines).Select(l => l.ItemId), Is.EqualTo(new[] { items[0] }));

        var uncommitted = await projection.GetComposedForApplicationAsync(
            appId, new BudgetLineFilter(Status: BudgetLineStatus.Uncommitted), CancellationToken.None);
        Assert.That(uncommitted.Tranches.SelectMany(t => t.Lines).Select(l => l.ItemId), Is.EqualTo(new[] { items[1] }));
    }

    [Test]
    public async Task FilterByValidationState_HasPending()
    {
        using var ctx = CreateContext($"filter-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m], ApplicationState.AgreementExecuted);
        var svc = DisbursementTestFactory.NewService(ctx, new AiComparison.InMemoryObjectStorage());
        var projection = NewProjection(ctx);
        await svc.CommitLineAsync(appId, items[0], Actor, CancellationToken.None);
        await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 50_000m, "TX-1", null,
            [new LineAllocationInput(items[0], 50_000m)]), Actor, CancellationToken.None);

        var pending = await projection.GetComposedForApplicationAsync(
            appId, new BudgetLineFilter(ValidationState: BudgetLineValidationState.HasPending), CancellationToken.None);

        Assert.That(pending.Tranches.SelectMany(t => t.Lines).Select(l => l.ItemId), Is.EqualTo(new[] { items[0] }));
    }

    [Test]
    public async Task FilterBySupplier_MatchesSelectedSupplier()
    {
        using var ctx = CreateContext($"filter-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m], ApplicationState.AgreementExecuted);
        var projection = NewProjection(ctx);
        var supplierId = await ctx.Items.AsNoTracking().Where(i => i.Id == items[0]).Select(i => i.SelectedSupplierId!.Value).SingleAsync();

        var matched = await projection.GetComposedForApplicationAsync(
            appId, new BudgetLineFilter(SupplierId: supplierId), CancellationToken.None);
        Assert.That(matched.Tranches.SelectMany(t => t.Lines), Is.Not.Empty);

        var none = await projection.GetComposedForApplicationAsync(
            appId, new BudgetLineFilter(SupplierId: 999_999), CancellationToken.None);
        Assert.That(none.Tranches.SelectMany(t => t.Lines), Is.Empty);
    }
}
