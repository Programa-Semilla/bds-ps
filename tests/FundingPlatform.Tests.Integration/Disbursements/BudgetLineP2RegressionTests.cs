using FundingPlatform.Domain.Enums;
using static FundingPlatform.Tests.Integration.Disbursements.TrancheTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 046 / SC-006 + FR-005 — a pre-P2 executed application (no tranche rows, CommitState default
/// Uncommitted, no line allocations) yields the P1 flat balances unchanged plus exactly one synthetic
/// tranche holding every line. Also a light scale sanity for the composed projection (T043).
/// </summary>
[TestFixture]
public class BudgetLineP2RegressionTests
{
    [Test]
    public async Task PreP2ExecutedApp_FlatBalanceUnchanged_OneSyntheticTranche()
    {
        using var ctx = CreateContext($"reg-{Guid.NewGuid():N}");
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, [100_000m, 200_000m], ApplicationState.AgreementExecuted);
        var projection = NewProjection(ctx);

        // Flat: Committed is the only new dimension and is 0 (no commits); the rest match P1.
        var flat = await projection.GetForApplicationAsync(appId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(flat.Allocated, Is.EqualTo(300_000m));
            Assert.That(flat.Committed, Is.EqualTo(0m));
            Assert.That(flat.Paid, Is.EqualTo(0m));
            Assert.That(flat.Validated, Is.EqualTo(0m));
            Assert.That(flat.PendingValidation, Is.EqualTo(0m));
            Assert.That(flat.Available, Is.EqualTo(300_000m));
        });

        // Composed: exactly one synthetic tranche (TrancheId null) with every line; totals reconcile.
        var composed = await projection.GetComposedForApplicationAsync(appId, null, CancellationToken.None);
        Assert.That(composed.Tranches, Has.Count.EqualTo(1));
        Assert.That(composed.Tranches[0].TrancheId, Is.Null);
        Assert.That(composed.Tranches[0].Lines.Select(l => l.ItemId), Is.EquivalentTo(items));
        Assert.That(composed.Participant.Allocated, Is.EqualTo(300_000m));
        Assert.That(composed.Tranches[0].Lines.All(l => l.CommitState == ItemCommitState.Uncommitted), Is.True);
    }

    [Test]
    public async Task ComposedProjection_ScalesToManyLines()
    {
        using var ctx = CreateContext($"reg-{Guid.NewGuid():N}");
        var prices = Enumerable.Range(1, 20).Select(i => (decimal)(i * 10_000)).ToArray();
        var (appId, items) = await SeedAppWithPricedItemsAsync(ctx, prices, ApplicationState.AgreementExecuted);
        var projection = NewProjection(ctx);

        var composed = await projection.GetComposedForApplicationAsync(appId, null, CancellationToken.None);

        Assert.That(composed.Tranches.SelectMany(t => t.Lines).ToList(), Has.Count.EqualTo(items.Count));
        Assert.That(composed.Participant.Allocated, Is.EqualTo(prices.Sum()));
    }
}
