using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 015 — guard for the post-deploy migration logic in
/// <c>SeedData.sql</c>. SCOPE LIMITATION: integration tests in this project use
/// the EF InMemory provider (see <see cref="ExchangeRateRepositoryTests"/> for
/// the rationale). The real SQL post-deploy block is exercised end-to-end by
/// the E2E AspireFixture run, not here. These tests document the expected
/// stamping/flagging behaviour as a regression check on the migration *logic*.
///
/// What the post-deploy block must do (FR-031, FR-032):
///   - CRC rows that lack ConvertedCrcAmount: stamp Price into ConvertedCrcAmount.
///   - Non-CRC rows lacking a snapshot: flag LegacyNeedsReview = 1.
///   - Re-running the block must not re-flag rows that have since been attached.
///
/// The C# helper <see cref="ApplyMigration"/> mirrors the SQL UPDATE statements
/// in <c>SeedData.sql</c> step 3a / 3b.
/// </summary>
[TestFixture]
public class MigrationTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task PostDeploy_StampsCrcRows_FlagsLegacyNonCrcRows()
    {
        var dbName = $"mig-stamp-{Guid.NewGuid():N}";

        // Pre-deploy: simulate two existing legacy rows. One CRC, one USD.
        // We bypass the rich-domain ctor's stamping by writing a CRC row that
        // explicitly has ConvertedCrcAmount NULL and a USD row with no snapshot.
        using (var ctx = CreateContext(dbName))
        {
            var crcLegacy = MakeLegacyQuotation(currency: "CRC", price: 750_000m);
            var usdLegacy = MakeLegacyQuotation(currency: "USD", price: 1000m);
            ctx.Quotations.AddRange(crcLegacy, usdLegacy);
            await ctx.SaveChangesAsync();
        }

        // Run the migration logic.
        using (var ctx = CreateContext(dbName))
        {
            await ApplyMigration(ctx);
        }

        using (var ctx = CreateContext(dbName))
        {
            var quotes = await ctx.Quotations.AsNoTracking().ToListAsync();
            var crc = quotes.Single(q => q.Currency == "CRC");
            var usd = quotes.Single(q => q.Currency == "USD");

            Assert.That(crc.ConvertedCrcAmount, Is.EqualTo(750_000m), "CRC row should be stamped with Price.");
            Assert.That(crc.LegacyNeedsReview, Is.False);
            Assert.That(usd.LegacyNeedsReview, Is.True, "USD row without snapshot must be flagged.");
            Assert.That(usd.ConvertedCrcAmount, Is.Null);
        }
    }

    [Test]
    public async Task PostDeploy_IsIdempotent_DoesNotReFlagAttachedRows()
    {
        var dbName = $"mig-idempotent-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            ctx.Quotations.Add(MakeLegacyQuotation(currency: "USD", price: 1000m));
            await ctx.SaveChangesAsync();
        }

        // First run: flags the USD row.
        using (var ctx = CreateContext(dbName))
        {
            await ApplyMigration(ctx);
        }

        // Admin attaches a historical rate, clearing the flag.
        using (var ctx = CreateContext(dbName))
        {
            var quote = await ctx.Quotations.SingleAsync();
            var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc,
                500m, 510m, DateTime.UtcNow.AddDays(-30), "admin");
            quote.AttachLegacyRate(rate.ToSnapshot(Domain.Enums.RateType.Buy), 500_000m);
            await ctx.SaveChangesAsync();
        }

        // Second migration run must not re-flag the now-attached row.
        using (var ctx = CreateContext(dbName))
        {
            await ApplyMigration(ctx);
        }

        using (var ctx = CreateContext(dbName))
        {
            var quote = await ctx.Quotations.AsNoTracking().SingleAsync();
            Assert.That(quote.LegacyNeedsReview, Is.False, "Re-running the migration must not re-flag attached rows.");
            Assert.That(quote.ConvertedCrcAmount, Is.EqualTo(500_000m));
            Assert.That(quote.Snapshot, Is.Not.Null);
        }
    }

    /// <summary>
    /// Mirrors <c>SeedData.sql</c> step 3a / 3b. The two UPDATEs are guarded so
    /// re-running is a no-op for already-stamped or already-attached rows.
    /// </summary>
    private static async Task ApplyMigration(AppDbContext ctx)
    {
        var crcUnstamped = await ctx.Quotations
            .Where(q => q.Currency == "CRC" && q.ConvertedCrcAmount == null)
            .ToListAsync();
        foreach (var q in crcUnstamped)
        {
            // Field-level write; mimics raw-SQL UPDATE.
            ctx.Entry(q).Property(p => p.ConvertedCrcAmount).CurrentValue = q.Price;
        }

        var nonCrcUnflagged = await ctx.Quotations
            .Where(q => q.Currency != "CRC"
                     && q.ConvertedCrcAmount == null
                     && !q.LegacyNeedsReview)
            .ToListAsync();
        foreach (var q in nonCrcUnflagged)
        {
            ctx.Entry(q).Property(p => p.LegacyNeedsReview).CurrentValue = true;
        }

        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Constructs a Quotation that simulates a pre-spec-015 row (no snapshot,
    /// no ConvertedCrcAmount stamped, regardless of currency). The constructor
    /// stamps CRC rows automatically; we override that here for the test by
    /// clearing the field via reflection-equivalent EF metadata writes.
    /// </summary>
    private static Quotation MakeLegacyQuotation(string currency, decimal price)
    {
        var q = new Quotation(
            supplierId: 1,
            supplierBranchId: 1,
            documentId: 1,
            price: price,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            currency: currency);
        // Force the CRC-stamp from the ctor back to "legacy state" so the
        // migration has work to do. The setter is private; reach it via the
        // backing field (reflection-equivalent of a raw SQL UPDATE).
        var prop = typeof(Quotation).GetProperty(
            nameof(Quotation.ConvertedCrcAmount),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        prop!.GetSetMethod(nonPublic: true)!.Invoke(q, new object?[] { null });
        return q;
    }
}
