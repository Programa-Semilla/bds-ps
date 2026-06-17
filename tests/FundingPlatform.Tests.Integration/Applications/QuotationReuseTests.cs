using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using FundingPlatform.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Applications;

/// <summary>
/// Spec 035 / US3 / T048 — quotation reuse within an application:
///   - <see cref="ApplicationService.ReuseQuotationAsync"/> creates a new quotation
///     row on a sibling item sharing the source's DocumentId (and supplier/branch),
///     with this item's own price.
///   - <see cref="ApplicationService.GetReusableQuotationsAsync"/> offers the OTHER
///     items' quotations, excluding the target item.
///   - reference-counted blob retention (research D5): removing one of two quotations
///     that share a document keeps the blob; removing the last reference deletes it.
///
/// Follows the project's integration-test convention of EF InMemory (see
/// <see cref="ApplicationServiceEditQuotationTests"/>); the SQL FK/index contract is
/// exercised end-to-end by the E2E suite.
/// </summary>
[TestFixture]
public class QuotationReuseTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Records every blob delete so retention behavior is observable.</summary>
    private sealed class RecordingObjectStorage : IObjectStorage
    {
        public List<string> Deletes { get; } = new();

        public Task<StoredObject> UploadAsync(FileCategory category, ObjectKey key, Stream content,
            string contentType, long? contentLength, CancellationToken ct)
            => Task.FromResult(new StoredObject(
                Container: key.Container, Key: key.Value,
                SizeBytes: contentLength ?? 0, ContentType: contentType,
                CreatedAt: DateTimeOffset.UtcNow, Provider: StorageProviderName.LocalFilesystem));
        public Task<Stream> OpenReadAsync(FileCategory category, ObjectKey key, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(FileCategory category, ObjectKey key, CancellationToken ct)
            => Task.FromResult(true);
        public Task DeleteAsync(FileCategory category, ObjectKey key, CancellationToken ct)
        {
            Deletes.Add(key.Value);
            return Task.CompletedTask;
        }
        public Task<StorageHandle> ResolveServingHandleAsync(FileCategory category, ObjectKey key,
            ServingMode preferred, CancellationToken ct)
            => Task.FromResult<StorageHandle>(
                new TimeLimitedUrlHandle(new Uri("about:blank"), DateTimeOffset.UtcNow,
                    ContentType: "application/octet-stream", Length: 0));
    }

    private static ApplicationService BuildService(AppDbContext ctx, IObjectStorage storage)
    {
        var rateRepo = new ExchangeRateRepository(ctx);
        var conversion = new ConversionService(rateRepo);
        var supplierRepo = new SupplierRepository(ctx);
        var appRepo = new ApplicationRepository(ctx);
        var supplierCatalog = new SupplierCatalogService(supplierRepo, appRepo,
            NullLogger<SupplierCatalogService>.Instance);
        var outboxWriter = Substitute.For<FundingPlatform.Application.Notifications.INotificationOutboxWriter>();
        var txScope = Substitute.For<FundingPlatform.Application.Notifications.IWorkflowTransactionScope>();
        var currencyRepo = new CurrencyRepository(ctx);

        return new ApplicationService(
            appRepo,
            new CompanyRepository(ctx),
            new CategoryRepository(ctx),
            supplierRepo,
            storage,
            new ImpactTemplateRepository(ctx),
            new SystemConfigurationRepository(ctx),
            new DocumentRepository(ctx),
            supplierCatalog,
            conversion,
            outboxWriter,
            txScope,
            NullLogger<ApplicationService>.Instance,
            publicCodeGenerator: null,
            comparisonCacheInvalidator: null,
            currencyRepository: currencyRepo);
    }

    private sealed record Seed(
        int ApplicationId, int ItemAId, int ItemBId, int SourceQuotationId,
        int DocumentId, string BlobKey);

    /// <summary>
    /// Seeds CRC + a category, an applicant, a Draft application with TWO items
    /// (A + B), a supplier (one branch), a Document, and one quotation on item A
    /// that references the document. Item B starts with no quotations.
    /// </summary>
    private static async Task<Seed> SeedTwoItemsOneQuotationAsync(AppDbContext ctx)
    {
        ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
        ctx.Categories.Add(new Category("Equipo", "desc", isActive: true));
        await ctx.SaveChangesAsync();

        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}", legalId: "1-2222-3333",
            firstName: "A", lastName: "B",
            email: $"a-{Guid.NewGuid():N}@example.com", phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var application = new AppEntity(applicant.Id, 1, null,"Test Co");
        application.AssignPublicCode(Helpers.TestPublicCodes.Next());
        var category = await ctx.Categories.FirstAsync();
        application.AddItem(new Item("Item A", category.Id));
        application.AddItem(new Item("Item B", category.Id));
        ctx.Applications.Add(application);
        await ctx.SaveChangesAsync();

        var supplier = Supplier.CreateDraft(
            legalId: "S-OWN", name: "Owned Supplier",
            createdByApplicantId: applicant.Id,
            firstBranchName: "Sucursal principal",
            firstBranchContactName: null, firstBranchEmail: null,
            firstBranchPhone: null, firstBranchAddressLine: null,
            firstBranchProvince: "San Jose",
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();

        // Canonical ObjectKey format: {container}/{owner}/{entity-id}/{suffix}.{ext}
        const string blobKey = "application-attachments/app-1/1/reuse.pdf";
        var doc = new Document("reuse.pdf", blobKey, 100, "application/pdf");
        ctx.Documents.Add(doc);
        await ctx.SaveChangesAsync();

        var itemA = application.Items.First(i => i.ProductName == "Item A");
        var itemB = application.Items.First(i => i.ProductName == "Item B");
        var branch = supplier.Branches.First();
        var quotation = new Quotation(
            supplierId: supplier.Id, supplierBranchId: branch.Id,
            documentId: doc.Id, price: 900m,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), currency: "CRC");
        itemA.AttachQuotation(supplier, branch, quotation);
        await ctx.SaveChangesAsync();

        return new Seed(application.Id, itemA.Id, itemB.Id, quotation.Id, doc.Id, blobKey);
    }

    [Test]
    public async Task ReuseQuotation_CreatesNewRowSharingDocumentId_WithOwnPrice()
    {
        var db = $"reuse-share-{Guid.NewGuid():N}";
        Seed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedTwoItemsOneQuotationAsync(ctx);
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, new RecordingObjectStorage());
            await sut.ReuseQuotationAsync(
                seed.ApplicationId, seed.ItemBId, seed.SourceQuotationId,
                price: 1500m, currency: "CRC",
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)));
        }

        using (var ctx = CreateContext(db))
        {
            var itemB = await ctx.Items.Include(i => i.Quotations)
                .FirstAsync(i => i.Id == seed.ItemBId);
            var reused = itemB.Quotations.Single();

            Assert.Multiple(() =>
            {
                Assert.That(reused.Id, Is.Not.EqualTo(seed.SourceQuotationId), "Reuse must create a NEW quotation row.");
                Assert.That(reused.DocumentId, Is.EqualTo(seed.DocumentId), "Reused quotation shares the source's document.");
                Assert.That(reused.Price, Is.EqualTo(1500m), "Reused quotation carries this line's own price.");
            });

            // The source quotation on item A is unchanged.
            var source = await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == seed.SourceQuotationId);
            Assert.That(source.Price, Is.EqualTo(900m));
        }
    }

    [Test]
    public async Task GetReusableQuotations_ReturnsSiblingQuotations_ExcludingTargetItem()
    {
        var db = $"reuse-list-{Guid.NewGuid():N}";
        Seed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedTwoItemsOneQuotationAsync(ctx);
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, new RecordingObjectStorage());

            // Item B (no quotation) sees item A's quotation as a reuse candidate.
            var forB = await sut.GetReusableQuotationsAsync(seed.ApplicationId, excludeItemId: seed.ItemBId);
            Assert.That(forB, Has.Count.EqualTo(1));
            Assert.That(forB[0].SourceQuotationId, Is.EqualTo(seed.SourceQuotationId));
            Assert.That(forB[0].SupplierName, Is.EqualTo("Owned Supplier"));
            Assert.That(forB[0].DocumentFileName, Is.EqualTo("reuse.pdf"));

            // Item A is the only quoted item → excluding it leaves no candidates.
            var forA = await sut.GetReusableQuotationsAsync(seed.ApplicationId, excludeItemId: seed.ItemAId);
            Assert.That(forA, Is.Empty);
        }
    }

    [Test]
    public async Task RemoveQuotation_KeepsBlob_WhenSiblingStillReferencesDocument()
    {
        var db = $"reuse-keep-{Guid.NewGuid():N}";
        Seed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedTwoItemsOneQuotationAsync(ctx);
        }

        var storage = new RecordingObjectStorage();

        // Reuse A's quotation on B (now two quotations share the document).
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, storage);
            await sut.ReuseQuotationAsync(
                seed.ApplicationId, seed.ItemBId, seed.SourceQuotationId,
                price: 1500m, currency: "CRC",
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)));
        }

        // Remove A's (originating) quotation — B still references the document.
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, storage);
            await sut.RemoveQuotationAsync(seed.ApplicationId, seed.ItemAId, seed.SourceQuotationId);
        }

        Assert.That(storage.Deletes, Is.Empty,
            "The shared blob must be retained while a sibling quotation still references it.");
    }

    [Test]
    public async Task RemoveQuotation_DeletesBlob_WhenLastReferenceRemoved()
    {
        var db = $"reuse-delete-{Guid.NewGuid():N}";
        Seed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedTwoItemsOneQuotationAsync(ctx);
        }

        var storage = new RecordingObjectStorage();

        int reusedQuotationId;
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, storage);
            await sut.ReuseQuotationAsync(
                seed.ApplicationId, seed.ItemBId, seed.SourceQuotationId,
                price: 1500m, currency: "CRC",
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)));
        }

        using (var ctx = CreateContext(db))
        {
            var itemB = await ctx.Items.Include(i => i.Quotations).FirstAsync(i => i.Id == seed.ItemBId);
            reusedQuotationId = itemB.Quotations.Single().Id;
        }

        // Remove BOTH quotations; the blob is deleted only when the last reference goes.
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, storage);
            await sut.RemoveQuotationAsync(seed.ApplicationId, seed.ItemAId, seed.SourceQuotationId);
        }
        Assert.That(storage.Deletes, Is.Empty, "Still referenced by B after removing A.");

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, storage);
            await sut.RemoveQuotationAsync(seed.ApplicationId, seed.ItemBId, reusedQuotationId);
        }

        Assert.That(storage.Deletes, Has.Count.EqualTo(1),
            "Removing the last reference deletes the shared blob exactly once.");
    }
}
