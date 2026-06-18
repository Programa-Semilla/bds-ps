using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Reviews;

/// <summary>
/// Spec 018 / SC-003 — reviewer-side LineCode invariants exercised through
/// <see cref="ReviewService.ReviewItemAsync"/> against the EF-managed
/// aggregate. Mirrors the tests for spec-002's review flow but adds the
/// LineCode preconditions per FR-012 / FR-013 / FR-014.
/// </summary>
[TestFixture]
public class LineCodeRequiredAndUniqueTests
{
    private AppDbContext _ctx = null!;
    private ApplicationRepository _repo = null!;
    private ReviewService _service = null!;

    [SetUp]
    public void Setup()
    {
        var dbName = $"linecode-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _ctx = new AppDbContext(options);
        _repo = new ApplicationRepository(_ctx);
        // Spec 021 — ReviewService now also depends on INotificationOutboxWriter
        // + IWorkflowTransactionScope. Not exercised by this test (line-code
        // validation runs on ReviewItemAsync which does not enqueue notifications);
        // substitute lightweight no-ops.
        var outboxWriter = NSubstitute.Substitute.For<FundingPlatform.Application.Notifications.INotificationOutboxWriter>();
        var txScope = NSubstitute.Substitute.For<FundingPlatform.Application.Notifications.IWorkflowTransactionScope>();
        _service = new ReviewService(_repo, outboxWriter, txScope,
            NullLogger<ReviewService>.Instance);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private async Task<(int appId, int item1Id, int item2Id, int supplierId)> SeedUnderReviewAsync()
    {
        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}",
            legalId: "1-1234-5678",
            firstName: "Daniel",
            lastName: "Centeno",
            email: $"a-{Guid.NewGuid():N}@example.com",
            phone: null,
            performanceScore: null);
        _ctx.Applicants.Add(applicant);

        var category = new Category("Equipment", "desc", isActive: true);
        _ctx.Categories.Add(category);
        await _ctx.SaveChangesAsync();

        var supplier = Supplier.CreateDraft(
            legalId: "3-101-0001", name: "Proveedor Test", createdByApplicantId: applicant.Id,
            firstBranchName: "HQ", firstBranchContactName: null, firstBranchEmail: null,
            firstBranchPhone: null, firstBranchAddressLine: null, firstBranchProvince: null,
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(supplier, SupplierVerificationStatus.Verified);
        _ctx.Suppliers.Add(supplier);
        await _ctx.SaveChangesAsync();

        var doc = new Document("quote.pdf", "/store/quote", 1024, "application/pdf");
        _ctx.Documents.Add(doc);
        await _ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, 1, null,"Test Company");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        var item1 = new Item("Laptop", category.Id);
        var item2 = new Item("Antena", category.Id);
        app.AddItem(item1);
        app.AddItem(item2);
        _ctx.Applications.Add(app);
        await _ctx.SaveChangesAsync();

        // Add quotations so Approve has a valid supplier path.
        item1.AddQuotation(supplier, supplier.Branches.First(), doc,
            price: 1000m, validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            currency: "CRC",
            deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
            warranty: new TimeDuration(12, DurationUnit.Months));
        item2.AddQuotation(supplier, supplier.Branches.First(), doc,
            price: 2000m, validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            currency: "CRC",
            deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
            warranty: new TimeDuration(12, DurationUnit.Months));

        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.UnderReview);
        await _ctx.SaveChangesAsync();

        return (app.Id, item1.Id, item2.Id, supplier.Id);
    }

    [Test]
    public async Task ReviewItem_BlankLineCode_ReturnsLineCodeRequired()
    {
        var (appId, itemId, _, supplierId) = await SeedUnderReviewAsync();

        var error = await _service.ReviewItemAsync(
            appId, itemId, "Approve", comment: null, selectedSupplierId: supplierId,
            lineCode: "  ", userId: "reviewer-1");

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo(UserFacingErrorCode.LineCodeRequired));
    }

    [Test]
    public async Task ReviewItem_NullLineCode_ReturnsLineCodeRequired()
    {
        var (appId, itemId, _, supplierId) = await SeedUnderReviewAsync();

        var error = await _service.ReviewItemAsync(
            appId, itemId, "Approve", comment: null, selectedSupplierId: supplierId,
            lineCode: null, userId: "reviewer-1");

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo(UserFacingErrorCode.LineCodeRequired));
    }

    [Test]
    public async Task ReviewItem_OverLengthLineCode_ReturnsLineCodeTooLong()
    {
        var (appId, itemId, _, supplierId) = await SeedUnderReviewAsync();

        var error = await _service.ReviewItemAsync(
            appId, itemId, "Approve", comment: null, selectedSupplierId: supplierId,
            lineCode: new string('A', 17), userId: "reviewer-1");

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo(UserFacingErrorCode.LineCodeTooLong));
    }

    [Test]
    public async Task ReviewItem_DuplicateWithinApplication_ReturnsLineCodeDuplicate()
    {
        var (appId, item1Id, item2Id, supplierId) = await SeedUnderReviewAsync();

        var first = await _service.ReviewItemAsync(
            appId, item1Id, "Approve", comment: null, selectedSupplierId: supplierId,
            lineCode: "T1-1", userId: "reviewer-1");
        Assert.That(first, Is.Null, "First assignment should succeed");

        var second = await _service.ReviewItemAsync(
            appId, item2Id, "Approve", comment: null, selectedSupplierId: supplierId,
            lineCode: "T1-1", userId: "reviewer-1");

        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Code, Is.EqualTo(UserFacingErrorCode.LineCodeDuplicate));
    }

    [Test]
    public async Task ReviewItem_DistinctCodes_BothPersist()
    {
        var (appId, item1Id, item2Id, supplierId) = await SeedUnderReviewAsync();

        var first = await _service.ReviewItemAsync(
            appId, item1Id, "Approve", comment: null, selectedSupplierId: supplierId,
            lineCode: "T1-1", userId: "reviewer-1");
        var second = await _service.ReviewItemAsync(
            appId, item2Id, "Approve", comment: null, selectedSupplierId: supplierId,
            lineCode: "T1-2", userId: "reviewer-1");

        Assert.That(first, Is.Null);
        Assert.That(second, Is.Null);

        var loaded = await _ctx.Applications
            .Include(a => a.Items)
            .FirstAsync(a => a.Id == appId);
        Assert.That(loaded.Items.Select(i => i.LineCode),
            Is.EquivalentTo(new[] { "T1-1", "T1-2" }));
    }

    [Test]
    public async Task ReviewItem_RequestMoreInfo_AllowsBlankLineCode()
    {
        // Per R-008 — RequestMoreInfo lets the reviewer iterate before deciding
        // on a code; a blank LineCode is allowed (and skipped) in that branch.
        var (appId, itemId, _, _) = await SeedUnderReviewAsync();

        var error = await _service.ReviewItemAsync(
            appId, itemId, "RequestMoreInfo", comment: "Need more docs",
            selectedSupplierId: null, lineCode: null, userId: "reviewer-1");

        Assert.That(error, Is.Null);
    }
}
