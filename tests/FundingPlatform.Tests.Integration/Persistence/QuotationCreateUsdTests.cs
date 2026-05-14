using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 015 / US1 — covers <see cref="ApplicationService.AddQuotationToExistingBranchAsync"/>
/// when an applicant submits a USD quotation. Asserts:
///   - The persisted row has Currency='USD', Price=1000, ConvertedCrcAmount=520_000.00
///   - Snapshot fields are populated from the latest published rate
///   - The source <see cref="ExchangeRate"/> row has IsUsed=true (FR-008)
///
/// SCOPE LIMITATION: this fixture uses the EF InMemory provider for parity with
/// other persistence tests in this project (see ExchangeRateRepositoryTests for
/// the rationale). The real SQL FK + unique-index constraints are exercised
/// via the AspireFixture E2E suite.
/// </summary>
[TestFixture]
public class QuotationCreateUsdTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DateTime Past(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    [Test]
    public async Task AddQuotation_UsdWithPublishedRate_PopulatesSnapshotAndStampsConvertedCrc()
    {
        var dbName = $"q-usd-{Guid.NewGuid():N}";
        int appId, itemId, supplierId, branchId;
        Guid rateId;

        // Arrange: applicant + application with a single Item + verified supplier + branch + published rate.
        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "LEG-1", firstName: "Ana", lastName: "Applicant",
                email: "ana@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id, "Test Company");
            application.AddItem(new Item("Server", category.Id, "specs"));
            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();
            appId = application.Id;
            itemId = application.Items[0].Id;

            var supplier = Supplier.CreateDraft(
                legalId: "TS-1",
                name: "Test Supplier",
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
            supplierId = supplier.Id;
            branchId = supplier.Branches.First().Id;

            ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
            ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar", 2, true, false, 2));
            var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m, Past(10), "admin");
            ctx.ExchangeRates.Add(rate);
            await ctx.SaveChangesAsync();
            rateId = rate.Id;
        }

        // Act: drive the use case via ApplicationService — the same surface the controller calls.
        using (var ctx = CreateContext(dbName))
        {
            var sut = BuildApplicationService(ctx);

            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            await sut.AddQuotationToExistingBranchAsync(
                appId, itemId, supplierId, branchId,
                price: 1000m, currency: "USD",
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                fileStream: stream, fileName: "q.pdf",
                contentType: "application/pdf", fileSize: 3);
        }

        // Assert: the persisted Quotation has the new spec-015 fields populated and
        // the source ExchangeRate is now flagged IsUsed.
        using (var ctx = CreateContext(dbName))
        {
            var quote = await ctx.Quotations.AsNoTracking().SingleAsync();
            Assert.That(quote.Currency, Is.EqualTo("USD"));
            Assert.That(quote.Price, Is.EqualTo(1000m));
            Assert.That(quote.ConvertedCrcAmount, Is.EqualTo(520_000.00m));
            Assert.That(quote.Snapshot, Is.Not.Null);
            Assert.That(quote.Snapshot!.RateRecordId, Is.EqualTo(rateId));
            Assert.That(quote.Snapshot.RateValue, Is.EqualTo(520m));
            Assert.That(quote.Snapshot.RateType, Is.EqualTo(RateType.Buy));
            Assert.That(quote.LegacyNeedsReview, Is.False);

            var rate = await ctx.ExchangeRates.AsNoTracking().SingleAsync();
            Assert.That(rate.IsUsed, Is.True, "Source rate must be marked used after the quotation persists (FR-008).");
        }
    }

    [Test]
    public async Task AddQuotation_UsdWithNoPublishedRate_ThrowsMissingRateException()
    {
        var dbName = $"q-usd-norate-{Guid.NewGuid():N}";
        int appId, itemId, supplierId, branchId;

        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "LEG-2", firstName: "Bo", lastName: "Bee",
                email: "bo@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id, "Test Company");
            application.AddItem(new Item("Server", category.Id, "specs"));
            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();
            appId = application.Id;
            itemId = application.Items[0].Id;

            var supplier = Supplier.CreateDraft(
                legalId: "TS-2",
                name: "Test Supplier",
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
            supplierId = supplier.Id;
            branchId = supplier.Branches.First().Id;

            ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
            ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar", 2, true, false, 2));
            // Deliberately no ExchangeRate rows.
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var sut = BuildApplicationService(ctx);

            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            Assert.That(async () =>
                await sut.AddQuotationToExistingBranchAsync(
                    appId, itemId, supplierId, branchId,
                    price: 1000m, currency: "USD",
                    validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                    fileStream: stream, fileName: "q.pdf",
                    contentType: "application/pdf", fileSize: 3),
                Throws.TypeOf<MissingRateException>());
        }
    }

    [Test]
    public async Task AddQuotation_Crc_ShortCircuits_NoSnapshotConvertedEqualsPrice()
    {
        var dbName = $"q-crc-{Guid.NewGuid():N}";
        int appId, itemId, supplierId, branchId;

        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "LEG-3", firstName: "C", lastName: "C",
                email: "c@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id, "Test Company");
            application.AddItem(new Item("Server", category.Id, "specs"));
            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();
            appId = application.Id;
            itemId = application.Items[0].Id;

            var supplier = Supplier.CreateDraft(
                legalId: "TS-3",
                name: "T2",
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
            supplierId = supplier.Id;
            branchId = supplier.Branches.First().Id;

            ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var sut = BuildApplicationService(ctx);

            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            await sut.AddQuotationToExistingBranchAsync(
                appId, itemId, supplierId, branchId,
                price: 750_000m, currency: "CRC",
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                fileStream: stream, fileName: "q.pdf",
                contentType: "application/pdf", fileSize: 3);
        }

        using (var ctx = CreateContext(dbName))
        {
            var quote = await ctx.Quotations.AsNoTracking().SingleAsync();
            Assert.That(quote.Currency, Is.EqualTo("CRC"));
            Assert.That(quote.Price, Is.EqualTo(750_000m));
            Assert.That(quote.ConvertedCrcAmount, Is.EqualTo(750_000m));
            Assert.That(quote.Snapshot, Is.Null);
            Assert.That(quote.LegacyNeedsReview, Is.False);
        }
    }

    /// <summary>
    /// Builds an <see cref="ApplicationService"/> wired against the in-memory <see cref="AppDbContext"/>
    /// with a no-op <see cref="IObjectStorage"/>. Mirrors the production wiring closely enough that
    /// the <see cref="ApplicationService.AddQuotationToExistingBranchAsync"/> code path, including the
    /// new spec-015 conversion routing, is exercised end-to-end at the application layer.
    /// </summary>
    private static ApplicationService BuildApplicationService(AppDbContext ctx)
    {
        var rateRepo = new ExchangeRateRepository(ctx);
        var conversion = new ConversionService(rateRepo);
        var supplierCatalog = new SupplierCatalogService(
            new SupplierRepository(ctx),
            new ApplicationRepository(ctx),
            NullLogger<SupplierCatalogService>.Instance);

        // Spec 021 — ApplicationService also depends on INotificationOutboxWriter
        // + IWorkflowTransactionScope. Not exercised by AddQuotationToExistingBranchAsync;
        // substitute lightweight no-ops.
        var outboxWriter = NSubstitute.Substitute.For<FundingPlatform.Application.Notifications.INotificationOutboxWriter>();
        var txScope = NSubstitute.Substitute.For<FundingPlatform.Application.Notifications.IWorkflowTransactionScope>();

        return new ApplicationService(
            new ApplicationRepository(ctx),
            new CategoryRepository(ctx),
            new SupplierRepository(ctx),
            new NoopObjectStorage(),
            new ImpactTemplateRepository(ctx),
            new SystemConfigurationRepository(ctx),
            new DocumentRepository(ctx),
            supplierCatalog,
            conversion,
            outboxWriter,
            txScope,
            NullLogger<ApplicationService>.Instance);
    }

    private sealed class NoopObjectStorage : IObjectStorage
    {
        public Task<StoredObject> UploadAsync(FileCategory category, ObjectKey key, Stream content,
            string contentType, long? contentLength, CancellationToken ct)
            => Task.FromResult(new StoredObject(
                Container: key.Container,
                Key: key.Value,
                SizeBytes: contentLength ?? 0,
                ContentType: contentType,
                CreatedAt: DateTimeOffset.UtcNow,
                Provider: StorageProviderName.LocalFilesystem));

        public Task<Stream> OpenReadAsync(FileCategory category, ObjectKey key, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> ExistsAsync(FileCategory category, ObjectKey key, CancellationToken ct)
            => Task.FromResult(true);

        public Task DeleteAsync(FileCategory category, ObjectKey key, CancellationToken ct)
            => Task.CompletedTask;

        public Task<StorageHandle> ResolveServingHandleAsync(FileCategory category, ObjectKey key,
            ServingMode preferred, CancellationToken ct)
            => Task.FromResult<StorageHandle>(new BackendStreamHandle(new MemoryStream(), "application/pdf", 0));
    }
}
