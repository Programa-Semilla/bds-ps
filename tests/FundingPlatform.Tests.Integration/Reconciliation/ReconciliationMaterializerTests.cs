using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using FundingPlatform.Tests.Integration.AiComparison;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FundingPlatform.Tests.Integration.Disbursements.DisbursementTestFactory;

namespace FundingPlatform.Tests.Integration.Reconciliation;

/// <summary>
/// Spec 048 / T024 — the materializer's identity-reconciliation behaviour on a seeded application:
/// insert-on-new, refresh-keeps-identity (no duplicate), fixed severity mapping, and auto-resolve
/// once the numbers match. Real-SQL enforcement (unique identity, CASCADE) is proven by the E2E suite.
/// </summary>
[TestFixture]
public class ReconciliationMaterializerTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    private static ReconciliationMaterializer NewMaterializer(AppDbContext ctx) =>
        new(ctx, NullLogger<ReconciliationMaterializer>.Instance);

    private static async Task SeedSentinelAsync(AppDbContext ctx)
    {
        if (!await ctx.Users.IgnoreQueryFilters().AnyAsync(u => u.IsSystemSentinel))
        {
            ctx.Users.Add(ApplicationUser.CreateSentinel("sentinel@programa-semilla.test"));
            await ctx.SaveChangesAsync();
        }
    }

    [Test]
    public async Task Materialize_MismatchedInvoice_InsertsOpenBlockingDiscrepancy_AndIsIdempotent()
    {
        var db = $"mat-insert-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();
        using var ctx = CreateContext(db);
        await SeedSentinelAsync(ctx);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);

        // Record a disbursement whose invoice differs from the paid amount by one colón (blocking b).
        var disb = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 500_000m, "TX-1", null), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, disb.Value, EvidenceKind.BankReceipt, 500_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, disb.Value, EvidenceKind.Invoice, 500_001m), Actor, CancellationToken.None);

        var materializer = NewMaterializer(ctx);
        await materializer.MaterializeAsync(appId, Actor, CancellationToken.None);

        var rows = await ctx.Discrepancies.AsNoTracking().Where(d => d.ApplicationId == appId).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        var row = rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.Comparison, Is.EqualTo(ReconciliationComparison.DisbursementVsInvoice));
            Assert.That(row.Severity, Is.EqualTo(DiscrepancySeverity.Blocking));
            Assert.That(row.State, Is.EqualTo(DiscrepancyState.Open));
            Assert.That(row.ScopeType, Is.EqualTo(DiscrepancyScopeType.Payment));
            Assert.That(row.ScopeEntityId, Is.EqualTo(disb.Value));
            Assert.That(row.Difference, Is.EqualTo(1m));
        });

        // Re-run: identity preserved, no duplicate row inserted (FR-003).
        await materializer.MaterializeAsync(appId, Actor, CancellationToken.None);
        var afterSecond = await ctx.Discrepancies.AsNoTracking().Where(d => d.ApplicationId == appId).ToListAsync();
        Assert.That(afterSecond, Has.Count.EqualTo(1));
        Assert.That(afterSecond[0].Id, Is.EqualTo(row.Id));
    }

    [Test]
    public async Task Materialize_AfterFix_AutoResolvesTheDiscrepancy()
    {
        var db = $"mat-resolve-{Guid.NewGuid():N}";
        var storage = new InMemoryObjectStorage();
        using var ctx = CreateContext(db);
        await SeedSentinelAsync(ctx);
        var appId = await SeedExecutedAppAsync(ctx);
        await SeedAllocationAsync(ctx, appId, 1_000_000m);
        var svc = NewService(ctx, storage);

        var disb = await svc.RecordAsync(new RecordDisbursementCommand(appId, Today, 500_000m, "TX-1", null), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, disb.Value, EvidenceKind.BankReceipt, 500_000m), Actor, CancellationToken.None);
        await svc.AttachEvidenceAsync(Ev(appId, disb.Value, EvidenceKind.Invoice, 500_001m), Actor, CancellationToken.None);

        var materializer = NewMaterializer(ctx);
        await materializer.MaterializeAsync(appId, Actor, CancellationToken.None);
        Assert.That(await ctx.Discrepancies.AsNoTracking().CountAsync(d => d.State == DiscrepancyState.Open), Is.EqualTo(1));

        // Fix the invoice so the numbers match, then re-materialize → the row auto-resolves (retained).
        await svc.AttachEvidenceAsync(Ev(appId, disb.Value, EvidenceKind.Invoice, 500_000m), Actor, CancellationToken.None);
        await materializer.MaterializeAsync(appId, Actor, CancellationToken.None);

        var rows = await ctx.Discrepancies.AsNoTracking().Where(d => d.ApplicationId == appId).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1)); // retained, never deleted
        Assert.That(rows[0].State, Is.EqualTo(DiscrepancyState.Resolved));
        Assert.That(rows[0].ResolvedAt, Is.Not.Null);
    }

    private static AttachDisbursementEvidenceCommand Ev(int appId, int disbId, EvidenceKind kind, decimal amount)
        => new(appId, disbId, kind, amount, "CRC", $"REF-{kind}", Today, Pdf(), $"{kind}.pdf", "application/pdf", 11);
}
