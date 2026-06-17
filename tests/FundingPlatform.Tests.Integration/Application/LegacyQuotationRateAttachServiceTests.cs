using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Application;

/// <summary>
/// Spec 015 / US6 / T601 — covers <see cref="LegacyQuotationRateAttachService"/>.
/// Asserts:
///   - Attaching a historical rate to a flagged quotation populates the
///     snapshot fields, computes ConvertedCrcAmount = price * rate.BuyRate,
///     clears the LegacyNeedsReview flag, and marks the source rate IsUsed.
///   - The audit-log entry carries
///     <see cref="MultiCurrencyAuditActions.QuotationLegacyRateAttached"/>.
///   - Attaching a rate to a non-flagged (already-attached) quotation throws
///     <see cref="InvalidOperationException"/> instead of silently overwriting
///     the existing snapshot. Documented behaviour: once a snapshot is on file
///     it is the system-of-record value (FR-013, FR-016) and is only changed
///     via the rate-change workflow, not the legacy-attach path.
///
/// SCOPE LIMITATION: uses the EF InMemory provider (matches the project's
/// integration-test convention; see <see cref="ExchangeRateRepositoryTests"/>).
/// The real SQL FK + unique-index constraints are exercised end-to-end via
/// the Aspire E2E suite (T602).
/// </summary>
[TestFixture]
public class LegacyQuotationRateAttachServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DateTime Past(int days) => DateTime.UtcNow.AddDays(-days);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = new();
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
        }
    }

    private static async Task<(int QuotationId, Guid RateId)> SeedFlaggedQuotationAsync(
        AppDbContext ctx, decimal price = 1000m)
    {
        ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
        ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar", 2, true, false, 2));

        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var applicant = new Applicant(
            userId: $"user-{Guid.NewGuid():N}",
            legalId: "LEG-LQ", firstName: "L", lastName: "Q",
            email: "lq@example.com", phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var application = new AppEntity(applicant.Id, 1, "Test Company");
        application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        application.AddItem(new Item("Server", category.Id));
        ctx.Applications.Add(application);
        await ctx.SaveChangesAsync();

        var supplier = Supplier.CreateDraft(
            legalId: "TS-LQ", name: "Legacy Supplier",
            createdByApplicantId: applicant.Id,
            firstBranchName: "Sede principal",
            firstBranchContactName: null,
            firstBranchEmail: null,
            firstBranchPhone: null,
            firstBranchAddressLine: null,
            firstBranchProvince: "San Jose",
            firstBranchShippingDetails: null,
            firstBranchWarrantyInfo: null);
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();

        var document = new Document("legacy.pdf", "container/key", 100, "application/pdf");
        ctx.Documents.Add(document);
        await ctx.SaveChangesAsync();

        // Legacy USD quotation lacking a snapshot — exactly the shape the
        // post-deploy migration would have flagged on a real upgrade.
        var quotation = new Quotation(
            supplierId: supplier.Id,
            supplierBranchId: supplier.Branches.First().Id,
            documentId: document.Id,
            price: price,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            currency: "USD");
        var item = application.Items.First();
        item.AttachQuotation(supplier, supplier.Branches.First(), quotation);
        // Force "legacy needs review" by clearing the stamping the ctor did and setting the flag.
        var convProp = typeof(Quotation).GetProperty(
            nameof(Quotation.ConvertedCrcAmount),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        convProp!.GetSetMethod(nonPublic: true)!.Invoke(quotation, new object?[] { null });
        var legacyProp = typeof(Quotation).GetProperty(
            nameof(Quotation.LegacyNeedsReview),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        legacyProp!.GetSetMethod(nonPublic: true)!.Invoke(quotation, new object?[] { true });
        await ctx.SaveChangesAsync();

        var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, Past(30), "admin");
        ctx.ExchangeRates.Add(rate);
        await ctx.SaveChangesAsync();

        return (quotation.Id, rate.Id);
    }

    [Test]
    public async Task AttachAsync_FlaggedQuotation_PopulatesSnapshotAndStampsConvertedCrc()
    {
        var dbName = $"legacy-attach-{Guid.NewGuid():N}";
        int quotationId;
        Guid rateId;

        using (var ctx = CreateContext(dbName))
        {
            (quotationId, rateId) = await SeedFlaggedQuotationAsync(ctx, price: 1000m);
        }

        var logger = new RecordingLogger<LegacyQuotationRateAttachService>();
        using (var ctx = CreateContext(dbName))
        {
            var repo = new QuotationLegacyRepository(ctx);
            var rateRepo = new ExchangeRateRepository(ctx);
            var sut = new LegacyQuotationRateAttachService(repo, rateRepo, logger);
            await sut.AttachAsync(quotationId, rateId, "actor-admin");
        }

        using (var ctx = CreateContext(dbName))
        {
            var quote = await ctx.Quotations.AsNoTracking().SingleAsync();
            Assert.That(quote.LegacyNeedsReview, Is.False, "Flag must be cleared after attach.");
            Assert.That(quote.ConvertedCrcAmount, Is.EqualTo(520_000.00m), "CRC = price * BuyRate.");
            Assert.That(quote.Snapshot, Is.Not.Null);
            Assert.That(quote.Snapshot!.RateRecordId, Is.EqualTo(rateId));
            Assert.That(quote.Snapshot.RateValue, Is.EqualTo(520m));
            Assert.That(quote.Snapshot.RateType, Is.EqualTo(RateType.Buy));

            var rate = await ctx.ExchangeRates.AsNoTracking().SingleAsync();
            Assert.That(rate.IsUsed, Is.True, "Source rate must be marked used.");
        }

        Assert.That(
            logger.Entries.Any(e => e.Contains(MultiCurrencyAuditActions.QuotationLegacyRateAttached)),
            Is.True,
            "Audit-log entry must carry the QuotationLegacyRateAttached action.");
    }

    [Test]
    public async Task AttachAsync_AlreadyAttachedQuotation_ThrowsInvalidOperation()
    {
        var dbName = $"legacy-already-{Guid.NewGuid():N}";
        int quotationId;
        Guid rateId;

        using (var ctx = CreateContext(dbName))
        {
            (quotationId, rateId) = await SeedFlaggedQuotationAsync(ctx, price: 1000m);
        }

        // First attach clears the flag.
        using (var ctx = CreateContext(dbName))
        {
            var sut = new LegacyQuotationRateAttachService(
                new QuotationLegacyRepository(ctx),
                new ExchangeRateRepository(ctx),
                new RecordingLogger<LegacyQuotationRateAttachService>());
            await sut.AttachAsync(quotationId, rateId, "actor-admin");
        }

        // Second attach must refuse — flag is already cleared, snapshot is on file.
        using (var ctx = CreateContext(dbName))
        {
            var sut = new LegacyQuotationRateAttachService(
                new QuotationLegacyRepository(ctx),
                new ExchangeRateRepository(ctx),
                new RecordingLogger<LegacyQuotationRateAttachService>());
            Assert.That(async () => await sut.AttachAsync(quotationId, rateId, "actor-admin"),
                Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public async Task AttachAsync_UnknownQuotation_Throws()
    {
        var dbName = $"legacy-unknown-q-{Guid.NewGuid():N}";
        Guid rateId;
        using (var ctx = CreateContext(dbName))
        {
            (_, rateId) = await SeedFlaggedQuotationAsync(ctx);
        }

        using (var ctx = CreateContext(dbName))
        {
            var sut = new LegacyQuotationRateAttachService(
                new QuotationLegacyRepository(ctx),
                new ExchangeRateRepository(ctx),
                new RecordingLogger<LegacyQuotationRateAttachService>());

            Assert.That(async () => await sut.AttachAsync(quotationId: 9_999_999, rateId, "actor"),
                Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public async Task AttachAsync_UnknownRate_Throws()
    {
        var dbName = $"legacy-unknown-r-{Guid.NewGuid():N}";
        int quotationId;
        using (var ctx = CreateContext(dbName))
        {
            (quotationId, _) = await SeedFlaggedQuotationAsync(ctx);
        }

        using (var ctx = CreateContext(dbName))
        {
            var sut = new LegacyQuotationRateAttachService(
                new QuotationLegacyRepository(ctx),
                new ExchangeRateRepository(ctx),
                new RecordingLogger<LegacyQuotationRateAttachService>());

            Assert.That(async () => await sut.AttachAsync(quotationId, Guid.NewGuid(), "actor"),
                Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public async Task ListAsync_ReturnsFlaggedRowsWithDisplayData()
    {
        var dbName = $"legacy-list-{Guid.NewGuid():N}";
        using (var ctx = CreateContext(dbName))
        {
            await SeedFlaggedQuotationAsync(ctx, price: 1234m);
        }

        using (var ctx = CreateContext(dbName))
        {
            var sut = new LegacyQuotationRateAttachService(
                new QuotationLegacyRepository(ctx),
                new ExchangeRateRepository(ctx),
                new RecordingLogger<LegacyQuotationRateAttachService>());

            var list = await sut.ListAsync();
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].Currency, Is.EqualTo("USD"));
            Assert.That(list[0].Price, Is.EqualTo(1234m));
            Assert.That(list[0].SupplierName, Is.EqualTo("Legacy Supplier"));
            Assert.That(list[0].ItemName, Is.EqualTo("Server"));
        }
    }
}
