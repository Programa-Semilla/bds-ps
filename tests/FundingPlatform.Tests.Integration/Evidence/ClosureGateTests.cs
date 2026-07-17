using FundingPlatform.Application.Evidence;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Evidence;

/// <summary>
/// Spec 047 / US3 — the budget-line closure gate over the persistence stack: happy close, each
/// blocking leg (missing doc, unvalidated payment, paid≠accepted), and reopen (off-ledger — no
/// balance change).
/// </summary>
[TestFixture]
public class ClosureGateTests
{
    private static string Db() => $"closure-gate-{Guid.NewGuid():N}";

    /// <summary>Builds an executed app with one line where SignedAcceptance is the only required doc,
    /// a validated payment of <paramref name="paid"/>, and a graph acceptance of <paramref name="accepted"/>
    /// fully allocated to the line.</summary>
    private static async Task<(int AppId, int ItemId)> ArrangeAsync(
        AppDbContext ctx, InMemoryObjectStorage storage, decimal paid, decimal accepted)
    {
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx, EvidenceType.SignedAcceptance);

        await EvidenceTestFactory.SeedValidatedDisbursementWithInvoiceAsync(ctx, appId, items[0], paid);
        await svc.AttachAsync(
            EvidenceTestFactory.AttachInvoice(appId, accepted, new[] { (items[0], accepted) }, type: EvidenceType.SignedAcceptance),
            EvidenceTestFactory.Actor, CancellationToken.None);

        return (appId, items[0]);
    }

    [Test]
    public async Task Close_HappyPath_Succeeds()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var (appId, itemId) = await ArrangeAsync(ctx, storage, paid: 100_000m, accepted: 100_000m);

        var result = await EvidenceTestFactory.NewClosureService(ctx)
            .CloseAsync(appId, itemId, "completo", EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.True, () => string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.That((await ctx.Items.FirstAsync(i => i.Id == itemId)).ClosureState, Is.EqualTo(ItemClosureState.Closed));
    }

    [Test]
    public async Task Close_AcceptanceShortfall_Blocks()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        // paid 100,000 vs accepted 99,928 (fully allocated so only the equality leg fires).
        var (appId, itemId) = await ArrangeAsync(ctx, storage, paid: 100_000m, accepted: 99_928m);

        var result = await EvidenceTestFactory.NewClosureService(ctx)
            .CloseAsync(appId, itemId, null, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.LineEqualityMismatch));
    }

    [Test]
    public async Task Close_MissingRequiredDoc_Blocks()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        // Require CreditNote (never present). Make paid == accepted (100,000 each) so legs b/c/d pass
        // and ONLY the missing-required-doc leg (a) blocks — an isolated leg-a test.
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx, EvidenceType.CreditNote);
        await EvidenceTestFactory.SeedValidatedDisbursementWithInvoiceAsync(ctx, appId, items[0], 100_000m);
        await svc.AttachAsync(
            EvidenceTestFactory.AttachInvoice(appId, 100_000m, new[] { (items[0], 100_000m) }, type: EvidenceType.SignedAcceptance),
            EvidenceTestFactory.Actor, CancellationToken.None);

        var result = await EvidenceTestFactory.NewClosureService(ctx)
            .CloseAsync(appId, items[0], null, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.MissingRequiredDocuments));
    }

    [Test]
    public async Task Close_RequiredEvidenceUnderAllocated_Blocks()
    {
        // Deep-review T1 — the fourth closure leg (each required graph evidence fully allocated).
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var svc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        // Require Invoice. Attach a graph Invoice of 100,000 but allocate only 60,000 to the line
        // (Σ 60,000 < amount 100,000 → not fully allocated). A matching validated payment + acceptance
        // of 60,000 make legs a (Invoice present via graph), b, c, and the payment-present guard pass;
        // only leg (d) fails.
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx, EvidenceType.Invoice);
        await svc.AttachAsync(
            EvidenceTestFactory.AttachInvoice(appId, 100_000m, new[] { (items[0], 60_000m) }, type: EvidenceType.Invoice),
            EvidenceTestFactory.Actor, CancellationToken.None);
        await EvidenceTestFactory.SeedValidatedDisbursementWithInvoiceAsync(ctx, appId, items[0], 60_000m);
        await svc.AttachAsync(
            EvidenceTestFactory.AttachInvoice(appId, 60_000m, new[] { (items[0], 60_000m) }, type: EvidenceType.SignedAcceptance),
            EvidenceTestFactory.Actor, CancellationToken.None);

        var result = await EvidenceTestFactory.NewClosureService(ctx)
            .CloseAsync(appId, items[0], null, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.RequiredEvidenceNotFullyAllocated));
    }

    [Test]
    public async Task Close_NoValidatedPayment_Blocks()
    {
        // Deep-review C3 / spec.md line 95 — a line with no attributed validated payment cannot close.
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx); // no required docs → completeness passes

        var result = await EvidenceTestFactory.NewClosureService(ctx)
            .CloseAsync(appId, items[0], null, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.NoPaymentToClose));
    }

    [Test]
    public async Task Close_UnvalidatedPayment_Blocks()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var (appId, items) = await EvidenceTestFactory.SeedExecutedAppWithLinesAsync(ctx, 1);
        await EvidenceTestFactory.SeedGlobalDefaultAsync(ctx); // no required docs → completeness passes
        await EvidenceTestFactory.SeedRecordedPaymentAsync(ctx, appId, items[0], 100_000m);

        var result = await EvidenceTestFactory.NewClosureService(ctx)
            .CloseAsync(appId, items[0], null, EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.PaymentNotValidated));
    }

    [Test]
    public async Task Reopen_UnlocksWithNoBalanceChange()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var (appId, itemId) = await ArrangeAsync(ctx, storage, paid: 100_000m, accepted: 100_000m);
        var closure = EvidenceTestFactory.NewClosureService(ctx);

        await closure.CloseAsync(appId, itemId, null, EvidenceTestFactory.Actor, CancellationToken.None);
        var ledgerBefore = await ctx.DisbursementLedgerEntries.CountAsync();

        var reopen = await closure.ReopenAsync(appId, itemId, "revisión", EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(reopen.Succeeded, Is.True);
        Assert.That((await ctx.Items.FirstAsync(i => i.Id == itemId)).ClosureState, Is.EqualTo(ItemClosureState.Open));
        Assert.That(await ctx.DisbursementLedgerEntries.CountAsync(), Is.EqualTo(ledgerBefore)); // off-ledger
    }

    [Test]
    public async Task ClosedLine_RejectsEvidenceWrites()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var evidenceSvc = EvidenceTestFactory.NewService(ctx, storage);
        var (appId, itemId) = await ArrangeAsync(ctx, storage, paid: 100_000m, accepted: 100_000m);
        await EvidenceTestFactory.NewClosureService(ctx)
            .CloseAsync(appId, itemId, null, EvidenceTestFactory.Actor, CancellationToken.None);

        // Attaching new evidence allocated to the closed line is refused (EvidenceLocked, FR).
        var attach = await evidenceSvc.AttachAsync(
            EvidenceTestFactory.AttachInvoice(appId, 5_000m, new[] { (itemId, 5_000m) }, type: EvidenceType.Other),
            EvidenceTestFactory.Actor, CancellationToken.None);

        Assert.That(attach.Succeeded, Is.False);
        Assert.That(attach.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.EvidenceLocked));
    }

    [Test]
    public async Task Reopen_WithoutReason_Refused()
    {
        await using var ctx = EvidenceTestFactory.CreateContext(Db());
        var storage = new InMemoryObjectStorage();
        var (appId, itemId) = await ArrangeAsync(ctx, storage, paid: 100_000m, accepted: 100_000m);
        var closure = EvidenceTestFactory.NewClosureService(ctx);
        await closure.CloseAsync(appId, itemId, null, EvidenceTestFactory.Actor, CancellationToken.None);

        var reopen = await closure.ReopenAsync(appId, itemId, "  ", EvidenceTestFactory.Actor, CancellationToken.None);
        Assert.That(reopen.Succeeded, Is.False);
        Assert.That(reopen.Errors[0].Code, Is.EqualTo(EvidenceReasons.Codes.ReopenReasonRequired));
    }
}
