using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Applications.Commands;
using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Applications;

/// <summary>
/// Spec 018 / SC-008 — applicant Create flow rejects blank / over-length
/// company names. Hits the real EF stack so the entity-level invariant +
/// command-side mapping path are both exercised.
/// </summary>
[TestFixture]
public class CompanyNameRequiredTests
{
    private AppDbContext _ctx = null!;
    private ApplicationService _service = null!;
    private int _applicantId;

    [SetUp]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"company-name-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _ctx = new AppDbContext(options);
        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}",
            legalId: "1-1234-5678",
            firstName: "Daniel",
            lastName: "Centeno",
            email: $"a-{Guid.NewGuid():N}@example.com",
            phone: null,
            performanceScore: null);
        _ctx.Applicants.Add(applicant);
        await _ctx.SaveChangesAsync();
        _applicantId = applicant.Id;

        var appRepo = new ApplicationRepository(_ctx);
        var categoryRepo = new CategoryRepository(_ctx);
        var supplierRepo = new SupplierRepository(_ctx);
        var impactRepo = new ImpactTemplateRepository(_ctx);
        var sysconfRepo = new SystemConfigurationRepository(_ctx);
        var docRepo = new DocumentRepository(_ctx);

        // ApplicationService also depends on IObjectStorage / SupplierCatalogService /
        // IConversionService which are only relevant for quotation operations; the
        // CreateApplication flow doesn't touch them. Mock them to keep the harness
        // light.
        var supplierCatalog = new SupplierCatalogService(supplierRepo, appRepo,
            NullLogger<SupplierCatalogService>.Instance);
        var conversion = Substitute.For<IConversionService>();
        var objectStorage = Substitute.For<IObjectStorage>();

        // Spec 021 — ApplicationService also depends on INotificationOutboxWriter
        // + IWorkflowTransactionScope. CreateApplication does not enqueue notifications
        // so mocks suffice; SubmitApplicationAsync (spec 021) needs them wired but is
        // not the surface exercised by this test.
        var outboxWriter = Substitute.For<FundingPlatform.Application.Notifications.INotificationOutboxWriter>();
        var txScope = Substitute.For<FundingPlatform.Application.Notifications.IWorkflowTransactionScope>();

        // Spec 021-feedback-session-may13 / FR-008 — Application.PublicCode is
        // EF-required. Stub the generator so CreateApplicationAsync stamps a
        // unique code before the first SaveChanges.
        var publicCodeGen = Substitute.For<FundingPlatform.Domain.Interfaces.IPublicCodeGenerator>();
        publicCodeGen.GenerateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next()));

        _service = new ApplicationService(
            appRepo, categoryRepo, supplierRepo, objectStorage, impactRepo,
            sysconfRepo, docRepo, supplierCatalog, conversion,
            outboxWriter, txScope,
            NullLogger<ApplicationService>.Instance,
            publicCodeGen);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Create_BlankCompanyName_ReturnsCompanyNameRequired()
    {
        var result = await _service.CreateApplicationAsync(
            new CreateApplicationCommand(_applicantId, "", 1), userId: "applicant-user");

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo(UserFacingErrorCode.CompanyNameRequired));
        Assert.That(result.ApplicationId, Is.EqualTo(0));
        Assert.That(await _ctx.Applications.AnyAsync(), Is.False, "No row should be persisted on validation failure");
    }

    [Test]
    public async Task Create_WhitespaceOnly_ReturnsCompanyNameRequired()
    {
        var result = await _service.CreateApplicationAsync(
            new CreateApplicationCommand(_applicantId, "    ", 1), userId: "applicant-user");

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo(UserFacingErrorCode.CompanyNameRequired));
    }

    [Test]
    public async Task Create_OverLengthCompanyName_ReturnsCompanyNameTooLong()
    {
        var name = new string('a', 201);

        var result = await _service.CreateApplicationAsync(
            new CreateApplicationCommand(_applicantId, name, 1), userId: "applicant-user");

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo(UserFacingErrorCode.CompanyNameTooLong));
    }

    [Test]
    public async Task Create_TrimsLeadingTrailingWhitespace_PersistsTrimmedValue()
    {
        var result = await _service.CreateApplicationAsync(
            new CreateApplicationCommand(_applicantId, "  Sazón Vegetariano  ", 1),
            userId: "applicant-user");

        Assert.That(result.Error, Is.Null);
        Assert.That(result.ApplicationId, Is.GreaterThan(0));

        var persisted = await _ctx.Applications.FirstAsync(a => a.Id == result.ApplicationId);
        Assert.That(persisted.CompanyName, Is.EqualTo("Sazón Vegetariano"));
    }

    [Test]
    public async Task Create_AcceptsCompanyName_AtMaxLength_200()
    {
        var name = new string('a', 200);

        var result = await _service.CreateApplicationAsync(
            new CreateApplicationCommand(_applicantId, name, 1), userId: "applicant-user");

        Assert.That(result.Error, Is.Null);
        Assert.That(result.ApplicationId, Is.GreaterThan(0));

        var persisted = await _ctx.Applications.FirstAsync(a => a.Id == result.ApplicationId);
        Assert.That(persisted.CompanyName.Length, Is.EqualTo(200));
    }
}
