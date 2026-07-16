using FundingPlatform.Application.Disbursements;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;
using static FundingPlatform.Tests.Integration.Disbursements.DisbursementTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 045 / T032 — append-only ledger invariants: validation posts exactly one immutable
/// Disbursement entry; a re-validation attempt is refused (no double-post at the logic level;
/// the filtered-unique index backstop is exercised by E2E); the Allocation snapshot equals
/// <see cref="ApplicationCurrencyTotal.Compute"/>.
/// </summary>
[TestFixture]
public class DisbursementLedgerTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    [Test]
    public async Task RecordAsync_PostsAllocationSnapshot_EqualToApplicationCurrencyTotal()
    {
        var db = $"disb-ledger-alloc-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx, crcQuotation: 500_000m);

        var result = await NewService(ctx, storage).RecordAsync(
            new RecordDisbursementCommand(appId, Today, 300_000m, "TX-1", null), Actor, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True);

        var allocation = await ctx.DisbursementLedgerEntries
            .SingleAsync(l => l.ApplicationId == appId && l.EntryType == LedgerEntryType.Allocation);
        Assert.That(allocation.Amount, Is.EqualTo(500_000m));

        var app = await ctx.Applications.Include(a => a.Items).ThenInclude(i => i.Quotations)
            .SingleAsync(a => a.Id == appId);
        Assert.That(ApplicationCurrencyTotal.Compute(app).Total, Is.EqualTo(500_000m),
            "the Allocation snapshot must equal the canonical CRC rollup");
    }

    [Test]
    public async Task Validate_PostsExactlyOneDisbursementLedgerEntry_AndBlocksSecond()
    {
        var db = $"disb-ledger-one-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);

        var recorded = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 300_000m, "TX-1", null), Actor, CancellationToken.None);
        var disbId = recorded.Value;

        await svc.AttachEvidenceAsync(EvidenceCmd(appId, disbId, EvidenceKind.BankReceipt, 300_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(EvidenceCmd(appId, disbId, EvidenceKind.Invoice, 300_000m), Actor, CancellationToken.None);

        var validated = await svc.ValidateAsync(appId, disbId, Actor, CancellationToken.None);
        Assert.That(validated.Succeeded, Is.True);

        var entries = await ctx.DisbursementLedgerEntries
            .Where(l => l.EntryType == LedgerEntryType.Disbursement && l.DisbursementId == disbId)
            .ToListAsync();
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Amount, Is.EqualTo(300_000m));

        // Re-validation is refused (already Validated → locked); no second entry posts.
        var second = await svc.ValidateAsync(appId, disbId, Actor, CancellationToken.None);
        Assert.That(second.Succeeded, Is.False);
        var after = await ctx.DisbursementLedgerEntries
            .CountAsync(l => l.EntryType == LedgerEntryType.Disbursement && l.DisbursementId == disbId);
        Assert.That(after, Is.EqualTo(1));
    }

    private static AttachDisbursementEvidenceCommand EvidenceCmd(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today, Pdf(), $"{kind}.pdf", "application/pdf", 11);
}
