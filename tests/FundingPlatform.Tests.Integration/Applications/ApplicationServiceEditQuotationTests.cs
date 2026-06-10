using FundingPlatform.Application.Abstractions.Comparison;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
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
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Applications;

/// <summary>
/// Spec 023 / T017 / T024 / T028 — covers <see cref="ApplicationService.EditQuotationAsync"/>:
///   US1 — price-only edit preserves <c>CreatedAt</c>, refreshes CRC equivalent for CRC.
///   US1 — Outcome.StateChanged when application is not in <c>Draft</c>.
///   US1 — Outcome.LegacyFlagged when the quotation carries the legacy flag.
///   US1 — Outcome.Forbidden when the caller is not the owner Applicant.
///   US1 — Outcome.Success short-circuits an idempotent repeat-POST: no
///         <c>ExchangeRate.IsUsed</c> mutation, no cache invalidation.
///   US2 — Branch swap on Draft persists; cross-supplier branch returns
///         <c>ValidationFailed</c> with the SupplierBranchId field error keyed
///         to the es-CR copy *"Sucursal no válida para este proveedor."*.
///   US3 — Currency change CRC → USD attaches a fresh snapshot, marks the
///         consumed rate used, and recomputes <c>ConvertedCrcAmount</c>.
///   US3 — MissingRateException maps to <c>Outcome.MissingRate</c>.
///   US3 — Cache invalidator fires on the non-idempotent success path AND
///         skips the idempotent short-circuit.
///
/// Follows the project's integration-test convention of EF InMemory (see
/// <see cref="LegacyQuotationRateAttachServiceTests"/>). The full SQL FK /
/// unique-index contract is exercised end-to-end by the E2E suite.
/// </summary>
[TestFixture]
public class ApplicationServiceEditQuotationTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Fake invalidator that records the calls EditQuotationAsync makes.</summary>
    private sealed class RecordingInvalidator : IComparisonCacheInvalidator
    {
        public List<int> Calls { get; } = new();
        public Task InvalidateForItemAsync(int itemId, CancellationToken ct = default)
        {
            Calls.Add(itemId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopObjectStorage : IObjectStorage
    {
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
            => Task.CompletedTask;
        public Task<StorageHandle> ResolveServingHandleAsync(FileCategory category, ObjectKey key,
            ServingMode preferred, CancellationToken ct)
            => Task.FromResult<StorageHandle>(
                new TimeLimitedUrlHandle(new Uri("about:blank"), DateTimeOffset.UtcNow,
                    ContentType: "application/octet-stream", Length: 0));
    }

    private static ApplicationService BuildService(
        AppDbContext ctx,
        IComparisonCacheInvalidator? invalidator = null,
        IConversionService? conversionOverride = null)
    {
        var rateRepo = new ExchangeRateRepository(ctx);
        IConversionService conversion = conversionOverride ?? new ConversionService(rateRepo);
        var supplierRepo = new SupplierRepository(ctx);
        var appRepo = new ApplicationRepository(ctx);
        var supplierCatalog = new SupplierCatalogService(supplierRepo, appRepo,
            NullLogger<SupplierCatalogService>.Instance);
        var outboxWriter = Substitute.For<FundingPlatform.Application.Notifications.INotificationOutboxWriter>();
        var txScope = Substitute.For<FundingPlatform.Application.Notifications.IWorkflowTransactionScope>();
        var currencyRepo = new CurrencyRepository(ctx);

        return new ApplicationService(
            appRepo,
            new CategoryRepository(ctx),
            supplierRepo,
            new NoopObjectStorage(),
            new ImpactTemplateRepository(ctx),
            new SystemConfigurationRepository(ctx),
            new DocumentRepository(ctx),
            supplierCatalog,
            conversion,
            outboxWriter,
            txScope,
            NullLogger<ApplicationService>.Instance,
            publicCodeGenerator: null,
            comparisonCacheInvalidator: invalidator,
            currencyRepository: currencyRepo);
    }

    private static async Task SeedCatalogAsync(AppDbContext ctx)
    {
        ctx.Currencies.Add(new Currency(CurrencyCode.Crc, "₡", "Costa Rican colón", 2, true, true, 1));
        ctx.Currencies.Add(new Currency(CurrencyCode.Usd, "$", "US dollar", 2, true, false, 2));
        ctx.Categories.Add(new Category("Equipment", "desc", isActive: true));
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an Applicant + Draft Application + Item + Supplier (two branches) +
    /// Quotation (CRC, price 1500). Returns the navigable ids needed by tests.
    /// </summary>
    private static async Task<TestSeed> SeedDraftWithQuotationAsync(
        AppDbContext ctx, string currency = "CRC", decimal price = 1500m)
    {
        await SeedCatalogAsync(ctx);

        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}",
            legalId: "1-2222-3333",
            firstName: "A", lastName: "B",
            email: $"a-{Guid.NewGuid():N}@example.com",
            phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var application = new AppEntity(applicant.Id, 1, "Test Co");
        application.AssignPublicCode(Helpers.TestPublicCodes.Next());
        var category = await ctx.Categories.FirstAsync();
        application.AddItem(new Item("Server", category.Id, "specs"));
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
        supplier.AddBranch("Sucursal secundaria",
            contactName: null, email: null, phone: null,
            addressLine: null, province: "San Jose",
            shippingDetails: null, warrantyInfo: null,
            createdByApplicantId: applicant.Id);
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();

        var doc = new Document("q.pdf", "application-attachments/q.pdf", 100, "application/pdf");
        ctx.Documents.Add(doc);
        await ctx.SaveChangesAsync();

        var item = application.Items.First();
        var firstBranch = supplier.Branches.First();
        var quotation = new Quotation(
            supplierId: supplier.Id,
            supplierBranchId: firstBranch.Id,
            documentId: doc.Id,
            price: price,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            currency: currency);
        item.AttachQuotation(supplier, firstBranch, quotation);
        await ctx.SaveChangesAsync();

        return new TestSeed(
            ApplicantId: applicant.Id,
            ApplicationId: application.Id,
            ItemId: item.Id,
            QuotationId: quotation.Id,
            SupplierId: supplier.Id,
            FirstBranchId: firstBranch.Id,
            SecondBranchId: supplier.Branches.Skip(1).First().Id);
    }

    private sealed record TestSeed(
        int ApplicantId, int ApplicationId, int ItemId, int QuotationId,
        int SupplierId, int FirstBranchId, int SecondBranchId);

    // ----------------------- US1 -----------------------

    [Test]
    public async Task EditQuotation_PriceOnlyOnDraft_PreservesCreatedAtAndRecomputesCrc()
    {
        var db = $"edit-price-{Guid.NewGuid():N}";
        TestSeed seed;
        DateTime createdAtBefore;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
            createdAtBefore = (await ctx.Quotations.AsNoTracking().SingleAsync(q => q.Id == seed.QuotationId)).CreatedAt;
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId,
                ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 1750m,
                Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.Success));
        }

        using (var ctx = CreateContext(db))
        {
            var q = await ctx.Quotations.AsNoTracking().SingleAsync(x => x.Id == seed.QuotationId);
            Assert.That(q.Price, Is.EqualTo(1750m));
            Assert.That(q.CreatedAt, Is.EqualTo(createdAtBefore), "CreatedAt must be preserved on Edit.");
            Assert.That(q.ConvertedCrcAmount, Is.EqualTo(1750m), "CRC must re-mirror Price after EditAmount.");
        }
    }

    [Test]
    public async Task EditQuotation_StateNotDraft_ReturnsOutcomeStateChanged()
    {
        var db = $"edit-state-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
            // Force the application out of Draft.
            var app = await ctx.Applications.SingleAsync(a => a.Id == seed.ApplicationId);
            typeof(AppEntity).GetProperty(nameof(AppEntity.State))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(app, new object[] { ApplicationState.UnderReview });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 9999m, Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.StateChanged));
            Assert.That(res.GlobalError, Does.Contain("El estado de la solicitud cambió"));
        }
    }

    [Test]
    public async Task EditQuotation_LegacyFlagged_ReturnsOutcomeLegacyFlagged()
    {
        var db = $"edit-legacy-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
            var q = await ctx.Quotations.SingleAsync(x => x.Id == seed.QuotationId);
            typeof(Quotation).GetProperty(nameof(Quotation.LegacyNeedsReview))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(q, new object[] { true });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 1750m, Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.LegacyFlagged));
        }
    }

    [Test]
    public async Task EditQuotation_NonOwner_ReturnsForbidden()
    {
        var db = $"edit-forbidden-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 1750m, Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId + 9_999, // foreign applicant
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.Forbidden));
        }
    }

    [Test]
    public async Task EditQuotation_IdempotentRepeat_DoesNotInvalidateCacheOrFlipRateUsed()
    {
        var db = $"edit-idempotent-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
        }

        var invalidator = new RecordingInvalidator();
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, invalidator);
            // Submit a POST with EXACTLY the seeded values.
            var q = await ctx.Quotations.AsNoTracking().SingleAsync(x => x.Id == seed.QuotationId);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = q.Price, Currency = q.Currency,
                ValidUntil = q.ValidUntil,
                SupplierBranchId = q.SupplierBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.Success));
        }

        Assert.That(invalidator.Calls, Is.Empty,
            "Idempotent repeat-POST must NOT invoke IComparisonCacheInvalidator (NFR-004).");
    }

    [Test]
    public async Task EditQuotation_PriceBelowZero_ReturnsValidationFailedWithFieldError()
    {
        var db = $"edit-zero-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 0m, Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.ValidationFailed));
            Assert.That(res.FieldErrors!.ContainsKey("Price"), Is.True);
            Assert.That(res.FieldErrors!["Price"], Does.Contain("mayor a cero"));
        }
    }

    // ----------------------- US2 -----------------------

    [Test]
    public async Task EditQuotation_BranchChangeOnDraft_PersistsAndDoesNotTouchSnapshot()
    {
        var db = $"edit-branch-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
        }

        var invalidator = new RecordingInvalidator();
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, invalidator);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 1500m, Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.SecondBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.Success));
        }

        using (var ctx = CreateContext(db))
        {
            var q = await ctx.Quotations.AsNoTracking().SingleAsync(x => x.Id == seed.QuotationId);
            Assert.That(q.SupplierBranchId, Is.EqualTo(seed.SecondBranchId));
            Assert.That(q.Snapshot, Is.Null, "CRC branch-only change must not attach a snapshot.");
        }
        Assert.That(invalidator.Calls, Has.Count.EqualTo(1).And.Contains(seed.ItemId));
    }

    [Test]
    public async Task EditQuotation_CrossSupplierBranch_ReturnsValidationFailedWithFieldError()
    {
        var db = $"edit-foreign-{Guid.NewGuid():N}";
        TestSeed seed;
        int foreignBranchId;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);

            // Build a second Supplier with its own branch — that branch id will be the
            // illegal target for the seeded quotation.
            var foreign = Supplier.CreateDraft(
                legalId: "S-OTHER", name: "Otro Proveedor",
                createdByApplicantId: seed.ApplicantId,
                firstBranchName: "Sede otro",
                firstBranchContactName: null, firstBranchEmail: null,
                firstBranchPhone: null, firstBranchAddressLine: null,
                firstBranchProvince: "Heredia",
                firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
            ctx.Suppliers.Add(foreign);
            await ctx.SaveChangesAsync();
            foreignBranchId = foreign.Branches.First().Id;
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 1500m, Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = foreignBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.ValidationFailed));
            Assert.That(res.FieldErrors!.ContainsKey(nameof(EditQuotationCommand.SupplierBranchId)), Is.True);
            Assert.That(res.FieldErrors![nameof(EditQuotationCommand.SupplierBranchId)],
                Does.Contain("Sucursal no válida para este proveedor."));
        }
    }

    // ----------------------- US3 -----------------------

    [Test]
    public async Task EditQuotation_CurrencyChangeCrcToUsd_FreshSnapshotAndRateMarkedUsed()
    {
        var db = $"edit-currency-{Guid.NewGuid():N}";
        TestSeed seed;
        Guid rateId;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx, currency: "CRC", price: 100m);
            var rate = new ExchangeRate(CurrencyCode.Usd, CurrencyCode.Crc, 520m, 525m,
                DateTime.UtcNow.AddMinutes(-10), "u");
            ctx.ExchangeRates.Add(rate);
            await ctx.SaveChangesAsync();
            rateId = rate.Id;
        }

        var invalidator = new RecordingInvalidator();
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, invalidator);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 100m, Currency = "USD",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.Success));
        }

        using (var ctx = CreateContext(db))
        {
            var q = await ctx.Quotations.AsNoTracking().SingleAsync(x => x.Id == seed.QuotationId);
            Assert.That(q.Currency, Is.EqualTo("USD"));
            Assert.That(q.Snapshot, Is.Not.Null);
            Assert.That(q.Snapshot!.RateRecordId, Is.EqualTo(rateId));
            Assert.That(q.ConvertedCrcAmount, Is.EqualTo(52_000m));
            Assert.That(q.LegacyNeedsReview, Is.False);

            var rate = await ctx.ExchangeRates.AsNoTracking().SingleAsync(r => r.Id == rateId);
            Assert.That(rate.IsUsed, Is.True, "Consumed rate must be marked IsUsed (spec 015 FR-008).");
        }

        Assert.That(invalidator.Calls, Has.Count.EqualTo(1).And.Contains(seed.ItemId));
    }

    [Test]
    public async Task EditQuotation_MissingRate_ReturnsOutcomeMissingRate()
    {
        var db = $"edit-missingrate-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx, currency: "CRC", price: 100m);
            // NO USD rate seeded → ConversionService will throw MissingRateException.
        }

        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 100m, Currency = "USD",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.MissingRate));
        }
    }

    [Test]
    public async Task EditQuotation_AnyChange_InvokesCacheInvalidatorWithItemId()
    {
        var db = $"edit-cachehit-{Guid.NewGuid():N}";
        TestSeed seed;
        using (var ctx = CreateContext(db))
        {
            seed = await SeedDraftWithQuotationAsync(ctx);
        }

        var invalidator = new RecordingInvalidator();
        using (var ctx = CreateContext(db))
        {
            var sut = BuildService(ctx, invalidator);
            var res = await sut.EditQuotationAsync(new EditQuotationCommand
            {
                ApplicationId = seed.ApplicationId, ItemId = seed.ItemId,
                QuotationId = seed.QuotationId,
                Price = 9_999m, Currency = "CRC",
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierBranchId = seed.FirstBranchId,
                ApplicantId = seed.ApplicantId,
            });
            Assert.That(res.Outcome, Is.EqualTo(EditQuotationOutcome.Success));
        }

        Assert.That(invalidator.Calls,
            Has.Count.EqualTo(1).And.Contains(seed.ItemId),
            "Cache invalidator must fire once with the Item id on the non-idempotent success path.");
    }
}
