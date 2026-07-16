using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using static FundingPlatform.Tests.Integration.Disbursements.TrancheTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 046 / US1 — tranche CRUD + assignment, the accent/case duplicate pre-check, the execution
/// freeze, and the composed-projection reconciliation (SC-001 / SC-003). InMemory harness (spec-045
/// precedent); the unique-index race + TINYINT materialization are covered by the E2E suite.
/// </summary>
[TestFixture]
public class TrancheServiceTests
{
    [Test]
    public async Task Create_Assign_DerivesTrancheAmounts()
    {
        using var ctx = CreateContext($"tr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m, 300_000m]);
        var svc = NewTrancheService(ctx);

        var t1 = await svc.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);
        var t2 = await svc.CreateAsync(appId, "Tramo 2", Actor, CancellationToken.None);
        Assert.That(t1.Succeeded && t2.Succeeded, Is.True);

        await svc.AssignItemAsync(appId, items[0], t1.Value, Actor, CancellationToken.None);
        await svc.AssignItemAsync(appId, items[1], t2.Value, Actor, CancellationToken.None);
        // items[2] left unassigned → synthetic

        var views = await svc.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(views.Single(v => v.Id == t1.Value).DerivedAmount, Is.EqualTo(100_000m));
            Assert.That(views.Single(v => v.Id == t2.Value).DerivedAmount, Is.EqualTo(200_000m));
        });
    }

    [Test]
    public async Task Composed_SyntheticTranche_PresentIffUnassignedLine_AndReconciles()
    {
        using var ctx = CreateContext($"tr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m, 300_000m]);
        var svc = NewTrancheService(ctx);
        var projection = NewProjection(ctx);

        var t1 = await svc.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);
        await svc.AssignItemAsync(appId, items[0], t1.Value, Actor, CancellationToken.None);
        // items[1], items[2] unassigned → synthetic present

        var composed = await projection.GetComposedForApplicationAsync(appId, null, CancellationToken.None);

        // SC-003 — participant Allocated = Σ line budgets = Σ tranche budgets.
        Assert.That(composed.Participant.Allocated, Is.EqualTo(600_000m));
        Assert.That(composed.Tranches.Sum(t => t.Balance.Allocated), Is.EqualTo(600_000m));

        var synthetic = composed.Tranches.SingleOrDefault(t => t.TrancheId is null);
        Assert.That(synthetic, Is.Not.Null, "synthetic tranche present while a line is unassigned");
        Assert.That(synthetic!.Balance.Allocated, Is.EqualTo(500_000m));
        Assert.That(synthetic.Name, Is.EqualTo(ComposedBalanceDefaults.SyntheticTrancheName));

        // Assign the rest → synthetic disappears.
        await svc.AssignItemAsync(appId, items[1], t1.Value, Actor, CancellationToken.None);
        await svc.AssignItemAsync(appId, items[2], t1.Value, Actor, CancellationToken.None);
        composed = await projection.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        Assert.That(composed.Tranches.Any(t => t.TrancheId is null), Is.False, "no synthetic once every line is assigned");
        Assert.That(composed.Tranches.Single().Balance.Allocated, Is.EqualTo(600_000m));
    }

    [Test]
    public async Task Create_DuplicateName_AccentAndCaseInsensitive_Rejected()
    {
        using var ctx = CreateContext($"tr-{Guid.NewGuid():N}");
        var (appId, _) = await SeedAppWithPricedItemsAsync(ctx, [100_000m]);
        var svc = NewTrancheService(ctx);

        await svc.CreateAsync(appId, "Tramo Único", Actor, CancellationToken.None);
        var dup = await svc.CreateAsync(appId, "tramo unico", Actor, CancellationToken.None);

        Assert.That(dup.Succeeded, Is.False);
        Assert.That(dup.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.TrancheNameInUse));
    }

    [Test]
    public async Task Rename_Succeeds()
    {
        using var ctx = CreateContext($"tr-{Guid.NewGuid():N}");
        var (appId, _) = await SeedAppWithPricedItemsAsync(ctx, [100_000m]);
        var svc = NewTrancheService(ctx);
        var t = await svc.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);

        var renamed = await svc.RenameAsync(appId, t.Value, "Fase inicial", Actor, CancellationToken.None);

        Assert.That(renamed.Succeeded, Is.True);
        var views = await svc.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.That(views.Single().Name, Is.EqualTo("Fase inicial"));
    }

    [Test]
    public async Task Delete_ReparentsLinesToSynthetic()
    {
        using var ctx = CreateContext($"tr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m]);
        var svc = NewTrancheService(ctx);
        var projection = NewProjection(ctx);
        var t = await svc.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);
        await svc.AssignItemAsync(appId, items[0], t.Value, Actor, CancellationToken.None);
        await svc.AssignItemAsync(appId, items[1], t.Value, Actor, CancellationToken.None);

        var del = await svc.DeleteAsync(appId, t.Value, Actor, CancellationToken.None);

        Assert.That(del.Succeeded, Is.True);
        var composed = await projection.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        Assert.That(composed.Tranches, Has.Count.EqualTo(1));
        Assert.That(composed.Tranches.Single().TrancheId, Is.Null); // all lines → synthetic
        Assert.That(composed.Tranches.Single().Balance.Allocated, Is.EqualTo(300_000m));
    }

    [Test]
    public async Task Create_Frozen_AfterExecution_Rejected()
    {
        using var ctx = CreateContext($"tr-{Guid.NewGuid():N}");
        var (appId, _) = await SeedAppWithPricedItemsAsync(ctx, [100_000m], ApplicationState.AgreementExecuted);
        var svc = NewTrancheService(ctx);

        var result = await svc.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.TrancheFrozen));
    }

    [Test]
    public async Task Assign_Frozen_AfterExecution_Rejected()
    {
        using var ctx = CreateContext($"tr-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m]);
        var svc = NewTrancheService(ctx);
        var t = await svc.CreateAsync(appId, "Tramo 1", Actor, CancellationToken.None);
        await ExecuteAsync(ctx, appId);

        var result = await svc.AssignItemAsync(appId, items[0], t.Value, Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(DisbursementReasons.Codes.TrancheFrozen));
    }
}
