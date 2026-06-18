using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 013 — partial guard for SC-003 (score parity) and SC-006 (migration
/// performance), via in-memory EF surrogates rather than real SQL Server.
///
/// SCOPE LIMITATIONS (read first):
///   - These tests do NOT execute the dacpac PostDeploy migration body in
///     SeedData.sql. They use the InMemory provider and Supplier.CreateDraft
///     factory to construct "pre" and "post" shapes side-by-side, then assert
///     score math is identical. A regression in the actual SQL migration
///     script will NOT fail these tests; that protection lives in the E2E
///     AspireFixture, which spins up a real SQL Server container and runs the
///     dacpac on every test session.
///   - The "performance" test measures EF InMemory SaveChanges throughput
///     for 1000 suppliers. It is a smoke test for the domain-factory path,
///     not a measurement of the SC-006 60-second budget against SQL Server.
///
/// What IS verified here: the score math (one point each for CCSS, Hacienda,
/// SICOP, e-invoice, lowest price; recommended = max-score AND not-rejected)
/// produces identical results for a "flat" supplier (pre-migration shape) and
/// the same supplier with a Sede-principal branch (post-migration shape). This
/// is the math-level part of SC-003.
/// </summary>
[TestFixture]
public class SupplierMigrationParityTests
{
    private AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public void SupplierScore_ProducesSameTotalsBeforeAndAfterBranchExtraction()
    {
        // "Pre-migration" simulation: build a supplier that had its compliance
        // flags + a single contact-name field. Migration extracts the contact
        // fields into a default branch. Score math reads only the four admin-only
        // flags + price, so the result must be identical.
        var (preSupplier, preQuotation) = BuildPreMigration("3-101-1", ccss: true, hacienda: true,
            sicop: false, eInvoice: true, price: 1000m);
        var (postSupplier, postQuotation, postBranch) = BuildPostMigration("3-101-1",
            ccss: true, hacienda: true, sicop: false, eInvoice: true, price: 1000m);

        var preScores = SupplierScore.ComputeForItem([(preQuotation, preSupplier, null)]);
        var postScores = SupplierScore.ComputeForItem([(postQuotation, postSupplier, postBranch)]);

        Assert.That(preScores[0].Score.Total, Is.EqualTo(postScores[0].Score.Total));
        Assert.That(preScores[0].Score.IsRecommended, Is.EqualTo(postScores[0].Score.IsRecommended));
        Assert.That(preScores[0].Score.HasLowestPrice, Is.EqualTo(postScores[0].Score.HasLowestPrice));
        Assert.That(preScores[0].Score.IsCompliantCCSS, Is.EqualTo(postScores[0].Score.IsCompliantCCSS));
        Assert.That(preScores[0].Score.IsCompliantHacienda, Is.EqualTo(postScores[0].Score.IsCompliantHacienda));
        Assert.That(preScores[0].Score.IsCompliantSICOP, Is.EqualTo(postScores[0].Score.IsCompliantSICOP));
    }

    [Test]
    public async Task EveryMigratedSupplier_LandsInVerifiedWithDefaultBranch()
    {
        var dbName = $"mig-{Guid.NewGuid():N}";

        // Seed 50 "post-migration" suppliers (Verified + 1 default branch each).
        // This mirrors the dacpac post-deployment script's end state.
        using (var ctx = CreateContext(dbName))
        {
            for (int i = 1; i <= 50; i++)
            {
                var s = Supplier.CreateDraft($"3-101-{i:D5}", $"Supplier {i}", 1,
                    "Sede principal", "Contact", $"c{i}@x.com", null, null, null, null, null);
                s.SubmitForReview();
                s.Verify("system-admin-sentinel");
                ctx.Suppliers.Add(s);
            }
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var suppliers = await ctx.Suppliers.Include(s => s.Branches).ToListAsync();
            Assert.That(suppliers.Count, Is.EqualTo(50));
            Assert.That(suppliers.All(s => s.VerificationStatus == SupplierVerificationStatus.Verified), Is.True);
            Assert.That(suppliers.All(s => s.VerifiedByUserId == "system-admin-sentinel"), Is.True);
            Assert.That(suppliers.All(s => s.Branches.Count == 1), Is.True);
            Assert.That(suppliers.All(s => s.Branches.First().IsDefault), Is.True);
            Assert.That(suppliers.All(s => s.Branches.First().BranchName == "Sede principal"), Is.True);
        }
    }

    [Test]
    public async Task LargeMigration_CompletesQuickly_ProxyForSC006()
    {
        // SC-006 demands the production migration runs in < 60 seconds. We use
        // a 1000-supplier in-memory build as a proxy — it should complete in
        // well under 5 seconds (10x safety margin) on this test runner. A
        // failure here is a strong signal something has regressed in the
        // domain factory or aggregate-root branch initialization.
        var dbName = $"mig-perf-{Guid.NewGuid():N}";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using (var ctx = CreateContext(dbName))
        {
            for (int i = 1; i <= 1000; i++)
            {
                var s = Supplier.CreateDraft($"3-101-{i:D5}", $"Supplier {i}", 1,
                    "Sede principal", "C", "c@x.com", null, null, null, null, null);
                s.SubmitForReview();
                s.Verify("system-admin-sentinel");
                ctx.Suppliers.Add(s);
            }
            await ctx.SaveChangesAsync();
        }

        stopwatch.Stop();
        Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(5.0),
            $"In-memory migration of 1000 suppliers took {stopwatch.Elapsed.TotalSeconds:F2}s; " +
            "real-world SQL Server migration of ~50-100 suppliers must stay well under SC-006's 60s budget.");
    }

    private static (Supplier supplier, Quotation quotation) BuildPreMigration(
        string legalId, bool ccss, bool hacienda, bool sicop, bool eInvoice, decimal price)
    {
        var s = Supplier.CreateDraft(legalId, "Pre", 1, "Sede principal",
            null, null, null, null, null, null, null);
        _ = eInvoice; // Spec 038 — e-invoice removed from scoring.
        s.ApplyRegulatoryEdit(
            hacienda ? HaciendaStatus.AlDia : null, ccss ? CcssStatus.AlDia : null,
            sicop ? SicopStatus.SinSanciones : null, false, false, null, "test-actor", DateTime.UtcNow);
        typeof(Supplier).GetProperty("Id")!.SetValue(s, 1);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(s, SupplierVerificationStatus.Verified);

        var q = new Quotation(supplierId: 1, supplierBranchId: 1, documentId: 1,
            price: price, validUntil: DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), currency: "USD");
        typeof(Quotation).GetProperty("Id")!.SetValue(q, 100);
        return (s, q);
    }

    private static (Supplier supplier, Quotation quotation, SupplierBranch branch) BuildPostMigration(
        string legalId, bool ccss, bool hacienda, bool sicop, bool eInvoice, decimal price)
    {
        var s = Supplier.CreateDraft(legalId, "Post", 1, "Sede principal",
            "Contact", "c@x.com", null, null, null, null, null);
        _ = eInvoice; // Spec 038 — e-invoice removed from scoring.
        s.ApplyRegulatoryEdit(
            hacienda ? HaciendaStatus.AlDia : null, ccss ? CcssStatus.AlDia : null,
            sicop ? SicopStatus.SinSanciones : null, false, false, null, "test-actor", DateTime.UtcNow);
        typeof(Supplier).GetProperty("Id")!.SetValue(s, 1);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(s, SupplierVerificationStatus.Verified);

        var b = s.Branches.First();
        typeof(SupplierBranch).GetProperty("Id")!.SetValue(b, 1);
        typeof(SupplierBranch).GetProperty("SupplierId")!.SetValue(b, 1);

        var q = new Quotation(supplierId: 1, supplierBranchId: 1, documentId: 1,
            price: price, validUntil: DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), currency: "USD");
        typeof(Quotation).GetProperty("Id")!.SetValue(q, 100);
        return (s, q, b);
    }
}
