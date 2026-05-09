using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 015 / US4 / T400 — application-level CRC rollup logic.
/// Builds an Application with mixed CRC + USD selected-supplier quotations
/// across multiple Items, then asserts that <see cref="ApplicationCurrencyTotal.Compute"/>
/// (the single source of truth shared by the Application Details view, the
/// applicant dashboard projection, and the reviewer queue projection) sums each
/// Item's selected-supplier <c>Quotation.ConvertedCrcAmount</c> in CRC and
/// excludes <c>LegacyNeedsReview = 1</c> rows.
///
/// SCOPE: parity with existing US1/US2 persistence tests — uses the EF InMemory
/// provider so the rollup logic is exercised end-to-end against the real entity
/// graph (Application → Items → Quotations) without needing the AspireFixture.
/// </summary>
[TestFixture]
public class RequestTotalRollupTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DateTime Past(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    [Test]
    public async Task RollupSumsConvertedCrc_AcrossMixedCurrencyItems_AndExcludesLegacyFlagged()
    {
        var dbName = $"rollup-{Guid.NewGuid():N}";

        // Arrange:
        //   Item 1: selected = CRC quotation 600,000 → contributes 600,000 CRC
        //   Item 2: selected = USD quotation 1000 @ buy 520 → contributes 520,000 CRC
        //   Item 3: selected = LEGACY USD quotation (no snapshot, flagged) → excluded
        //   Item 4: no selected supplier → skipped
        // Expected total = 1,120,000 CRC. HasNonCrc = true.
        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "ROLL-1",
                firstName: "Rollup", lastName: "Test",
                email: "rollup@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
            ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar", 2, true, false, 2));
            var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, Past(10), "admin");
            ctx.ExchangeRates.Add(rate);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id, "Test Company");
            application.AddItem(new Item("ItemCrc", category.Id, "specs1"));
            application.AddItem(new Item("ItemUsd", category.Id, "specs2"));
            application.AddItem(new Item("ItemLegacy", category.Id, "specs3"));
            application.AddItem(new Item("ItemUnselected", category.Id, "specs4"));
            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();

            var suppliers = Enumerable.Range(1, 4).Select(i =>
                Supplier.CreateDraft(
                    legalId: $"ROLL-S{i}",
                    name: $"Supplier {i}",
                    createdByApplicantId: applicant.Id,
                    firstBranchName: "Sede principal",
                    firstBranchContactName: null,
                    firstBranchEmail: null,
                    firstBranchPhone: null,
                    firstBranchAddressLine: null,
                    firstBranchProvince: "San Jose",
                    firstBranchShippingDetails: null,
                    firstBranchWarrantyInfo: null)).ToList();
            ctx.Suppliers.AddRange(suppliers);
            await ctx.SaveChangesAsync();

            var conversion = new ConversionService(new ExchangeRateRepository(ctx));

            // Item 1 — CRC quotation, attached + selected.
            var docCrc = new Document("crc.pdf", "key-crc", 1L, "application/pdf");
            ctx.Documents.Add(docCrc);
            await ctx.SaveChangesAsync();
            application.Items[0].AddQuotation(
                suppliers[0], suppliers[0].Branches.First(), docCrc,
                price: 600_000m,
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                currency: "CRC");
            await ctx.SaveChangesAsync();

            // Item 2 — USD quotation. Build via SetCurrencyAndAmountAsync so the snapshot stamps.
            var docUsd = new Document("usd.pdf", "key-usd", 1L, "application/pdf");
            ctx.Documents.Add(docUsd);
            await ctx.SaveChangesAsync();
            var qUsd = new Quotation(
                suppliers[1].Id,
                suppliers[1].Branches.First().Id,
                docUsd.Id,
                price: 1m,
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                currency: "CRC");
            await qUsd.SetCurrencyAndAmountAsync(CurrencyCode.Usd, 1000m, conversion);
            application.Items[1].AttachQuotation(suppliers[1], suppliers[1].Branches.First(), qUsd);
            await ctx.SaveChangesAsync();

            // Item 3 — LEGACY USD quotation: no snapshot, then flagged via the internal API.
            var docLegacy = new Document("legacy.pdf", "key-legacy", 1L, "application/pdf");
            ctx.Documents.Add(docLegacy);
            await ctx.SaveChangesAsync();
            application.Items[2].AddQuotation(
                suppliers[2], suppliers[2].Branches.First(), docLegacy,
                price: 2000m,
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                currency: "USD");
            var qLegacy = application.Items[2].Quotations.Single(q => q.SupplierId == suppliers[2].Id);
            // MarkLegacyNeedsReview is the internal API used by the post-deploy migration (FR-026).
            typeof(Quotation).GetMethod("MarkLegacyNeedsReview",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(qLegacy, null);
            await ctx.SaveChangesAsync();

            // Item 4 — quotation present but no supplier selected on the Item.
            var docFour = new Document("four.pdf", "key-four", 1L, "application/pdf");
            ctx.Documents.Add(docFour);
            await ctx.SaveChangesAsync();
            application.Items[3].AddQuotation(
                suppliers[3], suppliers[3].Branches.First(), docFour,
                price: 99_999m,
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                currency: "CRC");
            await ctx.SaveChangesAsync();

            // Set SelectedSupplierId on items 1, 2, 3 via Approve(...). Item 4 stays unselected.
            application.Items[0].Approve(suppliers[0].Id, "ok");
            application.Items[1].Approve(suppliers[1].Id, "ok");
            application.Items[2].Approve(suppliers[2].Id, "legacy-attached-later");
            await ctx.SaveChangesAsync();
        }

        // Act + Assert
        using (var ctx = CreateContext(dbName))
        {
            var app = await ctx.Applications
                .Include(a => a.Items)
                    .ThenInclude(i => i.Quotations)
                .SingleAsync();

            var (total, hasNonCrc) = ApplicationCurrencyTotal.Compute(app);

            Assert.That(total, Is.EqualTo(600_000m + 520_000m),
                "Rollup must sum the selected-supplier ConvertedCrcAmount of every non-legacy item.");
            Assert.That(hasNonCrc, Is.True,
                "HasNonCrc must reflect that at least one quotation on the application is non-CRC.");
        }
    }

    [Test]
    public async Task RollupReturnsNull_WhenNoItemHasASelectedSupplier()
    {
        var dbName = $"rollup-empty-{Guid.NewGuid():N}";

        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "ROLL-EMPTY",
                firstName: "Empty", lastName: "Test",
                email: "rollup-empty@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id, "Test Company");
            application.AddItem(new Item("ItemA", category.Id, "specsA"));
            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var app = await ctx.Applications
                .Include(a => a.Items)
                    .ThenInclude(i => i.Quotations)
                .SingleAsync();

            var (total, hasNonCrc) = ApplicationCurrencyTotal.Compute(app);
            Assert.That(total, Is.Null,
                "When no Item has a selected supplier, the rollup must report null (undetermined).");
            Assert.That(hasNonCrc, Is.False);
        }
    }
}
