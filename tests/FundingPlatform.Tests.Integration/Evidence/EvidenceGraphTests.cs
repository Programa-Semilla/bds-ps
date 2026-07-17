using FundingPlatform.Application.Evidence;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Evidence;

/// <summary>
/// Spec 047 / US1 — the evidence-graph service over the persistence stack: attach, M:N line
/// allocation (both directions), over-allocation + orphan refusals, and cascade delete of the
/// version + allocation children.
/// </summary>
[TestFixture]
public class EvidenceGraphTests
{
    private static string Db() => $"evidence-graph-{Guid.NewGuid():N}";

    [Test]
    public async Task Attach_OneInvoice_AcrossFourLines_AllLinked()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 4);

        var cmd = EvidenceTestFactory.AttachInvoice(appId, 400_000m,
            items.Select(i => (i, 100_000m)));
        var result = await svc.AttachAsync(cmd, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, () => string.Join("; ", result.Errors.Select(e => e.Message)));
        var allocations = await ctx.EvidenceLineAllocations.Where(a => a.EvidenceId == result.Value).ToListAsync();
        Assert.That(allocations, Has.Count.EqualTo(4));
        Assert.That(allocations.Sum(a => a.Amount), Is.EqualTo(400_000m));
        // v1 exists and is current.
        Assert.That(await ctx.EvidenceVersions.CountAsync(v => v.EvidenceId == result.Value && v.IsCurrent), Is.EqualTo(1));
    }

    [Test]
    public async Task Attach_FiveInvoices_OnOneLine_AllRetained()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        var line = items[0];

        for (var n = 0; n < 5; n++)
        {
            var cmd = EvidenceTestFactory.AttachInvoice(appId, 10_000m, new[] { (line, 10_000m) });
            var r = await svc.AttachAsync(cmd, EvidenceTestFactory.Actor, CancellationToken.None);
            Assert.That(r.Succeeded, Is.True);
        }

        // Five distinct evidence nodes, each allocated to the one line; per-line sum = 50,000.
        Assert.That(await ctx.Evidence.CountAsync(e => e.ApplicationId == appId), Is.EqualTo(5));
        Assert.That(await ctx.EvidenceLineAllocations.Where(a => a.ItemId == line).SumAsync(a => a.Amount),
            Is.EqualTo(50_000m));
    }

    [Test]
    public async Task Attach_OverAllocation_Refused()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 2);

        // Σ 500,000 > amount 400,000.
        var cmd = EvidenceTestFactory.AttachInvoice(appId, 400_000m,
            new[] { (items[0], 300_000m), (items[1], 200_000m) });
        var result = await svc.AttachAsync(cmd, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.AllocationExceedsAmount));
        Assert.That(storage.StoredCount, Is.EqualTo(0)); // refused before upload — no blob leak
    }

    [Test]
    public async Task Attach_Orphan_NoLineNoDisbursement_Refused()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, _) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);

        var cmd = EvidenceTestFactory.AttachInvoice(appId, 100_000m, Array.Empty<(int, decimal)>());
        var result = await svc.AttachAsync(cmd, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.Orphaned));
    }

    [Test]
    public async Task Attach_AcceptanceWithoutPayment_Stored()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);

        var cmd = EvidenceTestFactory.AttachInvoice(appId, 100_000m, new[] { (items[0], 100_000m) },
            disbursementId: null, type: EvidenceType.SignedAcceptance);
        var result = await svc.AttachAsync(cmd, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True);
        var e = await ctx.Evidence.FirstAsync(x => x.Id == result.Value);
        Assert.That(e.Type, Is.EqualTo(EvidenceType.SignedAcceptance));
        Assert.That(e.DisbursementId, Is.Null);
    }

    [Test]
    public async Task Delete_CascadesVersionsAndAllocations()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);

        var cmd = EvidenceTestFactory.AttachInvoice(appId, 100_000m, new[] { (items[0], 100_000m) });
        var attach = await svc.AttachAsync(cmd, EvidenceTestFactory.Actor, CancellationToken.None);
        var id = attach.Value;

        var del = await svc.DeleteAsync(appId, id, EvidenceTestFactory.Actor, CancellationToken.None);
        Assert.That(del.Succeeded, Is.True);
        Assert.That(await ctx.Evidence.AnyAsync(e => e.Id == id), Is.False);
        Assert.That(await ctx.EvidenceVersions.AnyAsync(v => v.EvidenceId == id), Is.False);
        Assert.That(await ctx.EvidenceLineAllocations.AnyAsync(a => a.EvidenceId == id), Is.False);
    }
}
