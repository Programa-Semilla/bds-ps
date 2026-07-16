using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;
using static FundingPlatform.Tests.Integration.Disbursements.DisbursementTestFactory;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 045 / T022 — round-trips each TINYINT enum column (State/Kind/EntryType) through the
/// EF <c>HasConversion&lt;byte&gt;()</c> mappings. On the InMemory provider this proves the
/// value mapping; the <b>real-SQL Byte→Int32 materialization</b> that InMemory hides (the
/// 035/040 lesson) is exercised by the E2E suite against SQL Server (every Disbursement record/
/// list/validate materializes these columns from a real database).
/// </summary>
[TestFixture]
public class DisbursementEnumMaterializationTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    [Test]
    public async Task StateKindAndEntryType_RoundTrip_ThroughByteConversion()
    {
        var db = $"disb-enum-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();

        using var ctx = CreateContext(db);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);

        // A clean disbursement, proven + validated → State=Validated + a Disbursement ledger entry.
        var clean = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 500_000m, "TX-1", null), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, clean.Value, EvidenceKind.BankReceipt, 500_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, clean.Value, EvidenceKind.Invoice, 500_000m), Actor, CancellationToken.None);
        await svc.ValidateAsync(appId, clean.Value, Actor, CancellationToken.None);

        // A mismatched disbursement → State=Inconsistent; its evidence Kind=Invoice.
        var bad = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 100_000m, "TX-2", null), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, bad.Value, EvidenceKind.Invoice, 200_000m), Actor, CancellationToken.None);

        var validated = await ctx.Disbursements.AsNoTracking().SingleAsync(d => d.Id == clean.Value);
        var inconsistent = await ctx.Disbursements.AsNoTracking().SingleAsync(d => d.Id == bad.Value);
        var invoiceEvidence = await ctx.DisbursementEvidence.AsNoTracking()
            .SingleAsync(e => e.DisbursementId == bad.Value && e.Kind == EvidenceKind.Invoice);
        var allocationEntry = await ctx.DisbursementLedgerEntries.AsNoTracking()
            .SingleAsync(l => l.ApplicationId == appId && l.EntryType == LedgerEntryType.Allocation);
        var disbursementEntry = await ctx.DisbursementLedgerEntries.AsNoTracking()
            .SingleAsync(l => l.DisbursementId == clean.Value && l.EntryType == LedgerEntryType.Disbursement);

        Assert.Multiple(() =>
        {
            Assert.That(validated.State, Is.EqualTo(DisbursementState.Validated));
            Assert.That(inconsistent.State, Is.EqualTo(DisbursementState.Inconsistent));
            Assert.That(invoiceEvidence.Kind, Is.EqualTo(EvidenceKind.Invoice));
            Assert.That(allocationEntry.EntryType, Is.EqualTo(LedgerEntryType.Allocation));
            Assert.That(disbursementEntry.EntryType, Is.EqualTo(LedgerEntryType.Disbursement));
        });
    }

    private static AttachDisbursementEvidenceCommand Ev(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today, Pdf(), $"{kind}.pdf", "application/pdf", 11);
}
