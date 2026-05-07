using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 015 / US2 — covers <see cref="ApplicationService.AddQuotationToExistingBranchAsync"/>
/// when an applicant submits a CRC quotation. Asserts the CRC short-circuit:
///   - Persisted row has Currency='CRC', Price = ConvertedCrcAmount
///   - Snapshot fields are NULL (no rate consulted)
///   - LegacyNeedsReview is false
///   - The conversion service is never invoked (verified indirectly: there are
///     no <see cref="ExchangeRate"/> rows seeded — a CRC save must not throw).
///
/// SCOPE LIMITATION: this fixture uses the EF InMemory provider for parity with
/// other persistence tests in this project (see ExchangeRateRepositoryTests for
/// the rationale). The real SQL FK + unique-index constraints are exercised
/// via the AspireFixture E2E suite (<c>ApplicantCrcQuoteE2E</c>).
/// </summary>
[TestFixture]
public class QuotationCreateCrcTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Test]
    public async Task AddQuotation_Crc_PersistsShortCircuit_NoSnapshotConvertedEqualsPrice()
    {
        var dbName = $"q-crc-{Guid.NewGuid():N}";
        int appId, itemId, supplierId, branchId;

        // Arrange: applicant + application with one Item + verified supplier + branch.
        // Deliberately NO ExchangeRate rows are seeded — a CRC quotation must save
        // without consulting the rate catalog (FR-013, FR-016).
        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "LEG-CRC-1", firstName: "Carla", lastName: "CRC",
                email: "carla@example.com", phone: null, performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();

            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id);
            application.AddItem(new Item("Server", category.Id, "specs"));
            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();
            appId = application.Id;
            itemId = application.Items[0].Id;

            var supplier = Supplier.CreateDraft(
                legalId: "TS-CRC-1",
                name: "CRC Supplier",
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
            await ctx.SaveChangesAsync();
        }

        // Act: drive the use case via ApplicationService — the same surface the controller calls.
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

        // Assert: the persisted Quotation reflects the CRC short-circuit.
        using (var ctx = CreateContext(dbName))
        {
            var quote = await ctx.Quotations.AsNoTracking().SingleAsync();
            Assert.That(quote.Currency, Is.EqualTo("CRC"));
            Assert.That(quote.Price, Is.EqualTo(750_000m));
            Assert.That(quote.ConvertedCrcAmount, Is.EqualTo(750_000m),
                "CRC short-circuit: ConvertedCrcAmount must equal Price.");
            Assert.That(quote.Snapshot, Is.Null,
                "CRC short-circuit: no rate snapshot should be persisted.");
            Assert.That(quote.LegacyNeedsReview, Is.False);

            // Sanity: no ExchangeRate row was created or used. CRC must never hit the rate catalog.
            var rateCount = await ctx.ExchangeRates.AsNoTracking().CountAsync();
            Assert.That(rateCount, Is.EqualTo(0),
                "CRC quotation must not touch dbo.ExchangeRates.");
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
