using FundingPlatform.Application.DocRules;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Evidence;

/// <summary>
/// Spec 047 / US2 — the required-document rule matrix + the both-source per-line completeness
/// projection (graph evidence ∪ validated-disbursement evidence, research D1).
/// </summary>
[TestFixture]
public class DocumentRuleMatrixTests
{
    private static string Db() => $"docrule-matrix-{Guid.NewGuid():N}";

    [Test]
    public async Task Upsert_IsOnePerCategory_AndFullReplace()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var svc = EvidenceTestFactory.NewDocRuleService(ctx);

        await svc.UpsertAsync(new UpsertDocumentRuleCommand(null, new[]
        {
            new DocumentRuleTypeSelection(EvidenceType.Invoice, true),
            new DocumentRuleTypeSelection(EvidenceType.BankReceipt, true),
        }), EvidenceTestFactory.Actor, CancellationToken.None);

        // Second upsert on the same key (global default) replaces — not a duplicate set.
        await svc.UpsertAsync(new UpsertDocumentRuleCommand(null, new[]
        {
            new DocumentRuleTypeSelection(EvidenceType.SignedAcceptance, true),
        }), EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(await ctx.DocumentRuleSets.CountAsync(s => s.CategoryId == null), Is.EqualTo(1));
        var resolver = await svc.BuildResolverAsync(CancellationToken.None);
        Assert.That(resolver.RequiredFor(null), Is.EquivalentTo(new[] { EvidenceType.SignedAcceptance }));
    }

    [Test]
    public async Task Resolver_CategoryFallsBackToGlobalDefault()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var svc = EvidenceTestFactory.NewDocRuleService(ctx);
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx, EvidenceType.Invoice, EvidenceType.SignedAcceptance);

        var resolver = await svc.BuildResolverAsync(CancellationToken.None);

        // A category with no set of its own falls back to the global default.
        Assert.That(resolver.RequiredFor(999), Is.EquivalentTo(new[] { EvidenceType.Invoice, EvidenceType.SignedAcceptance }));
    }

    [Test]
    public async Task Completeness_LineWithOnlyBankReceipt_ShowsInvoiceAndAcceptanceMissing()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var evidenceSvc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx,
            EvidenceType.BankReceipt, EvidenceType.Invoice, EvidenceType.SignedAcceptance);

        // Attach only a graph bank receipt to the line.
        await evidenceSvc.AttachAsync(
            EvidenceTestFactory.AttachInvoice(appId, 100_000m, new[] { (items[0], 100_000m) },
                type: EvidenceType.BankReceipt),
            EvidenceTestFactory.Actor, CancellationToken.None);

        var completeness = await EvidenceTestFactory.NewCompletenessProjection(ctx)
            .GetForApplicationAsync(appId, CancellationToken.None);

        var line = completeness[items[0]];
        Assert.That(line.EvidenceIncomplete, Is.True);
        Assert.That(line.Missing, Is.EquivalentTo(new[] { EvidenceType.Invoice, EvidenceType.SignedAcceptance }));
        Assert.That(line.Present, Does.Contain(EvidenceType.BankReceipt));
    }

    [Test]
    public async Task Completeness_ValidatedDisbursementInvoice_CountsPresent()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx, EvidenceType.Invoice);

        // No graph evidence — the Invoice presence must come from the validated disbursement (D1).
        await EvidenceTestFactory.SeedValidatedDisbursementWithInvoiceAsync(ctx, appId, items[0], 100_000m);

        var completeness = await EvidenceTestFactory.NewCompletenessProjection(ctx)
            .GetForApplicationAsync(appId, CancellationToken.None);

        var line = completeness[items[0]];
        Assert.That(line.Present, Does.Contain(EvidenceType.Invoice));
        Assert.That(line.EvidenceIncomplete, Is.False);
    }
}
