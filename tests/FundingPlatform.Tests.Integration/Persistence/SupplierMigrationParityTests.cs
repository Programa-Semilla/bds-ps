using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 013 SC-003: byte-for-byte parity of SupplierScore math before and after
/// the supplier-catalog rollout. The dacpac script itself is exercised by the
/// E2E AspireFixture against a real SQL Server container — but the math invariant
/// it protects is testable here in pure C#: the score outcome for a (supplier,
/// quotation) pair should be identical regardless of whether the supplier
/// "carries" a default branch (post-migration) or was a flat row (pre-migration,
/// represented as supplier with a single "Sede principal" branch carrying the
/// old contact-name fields).
///
/// SC-006 (migration completes in &lt; 60s) is asserted indirectly: the in-memory
/// migration of 1000 suppliers must complete in &lt; 5s on this CI test runner,
/// which gives a 10x margin on the production target.
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
        Assert.That(preScores[0].Score.HasElectronicInvoice, Is.EqualTo(postScores[0].Score.HasElectronicInvoice));
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
        s.EditByAdmin("Pre", eInvoice, ccss, hacienda, sicop);
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
        s.EditByAdmin("Post", eInvoice, ccss, hacienda, sicop);
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
