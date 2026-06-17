using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.AiComparison;
using FundingPlatform.Infrastructure.AiComparison.Anthropic;
using FundingPlatform.Infrastructure.AiComparison.Redaction;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.AiComparison;

/// <summary>
/// Spec 020 regression coverage:
///   1) Extract responses missing <c>fields.currencyCode</c> are backfilled
///      from the platform's authoritative supplier currency before validation
///      (the Anthropic model intermittently omits the field).
///   2) Failure-path audit rows are persisted to the DB without the caller
///      having to invoke <c>SaveChangesAsync</c> — the orchestrator now owns
///      its own commit boundary via <c>IUnitOfWork</c>.
/// </summary>
[TestFixture]
public class ComparisonOrchestratorRegressionTests
{
    private AppDbContext _ctx = null!;
    private string _tempExtractFixture = null!;

    [SetUp]
    public void Setup()
    {
        StubAiClient.ResetCallCounters();

        var dbName = $"orch-reg-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _ctx = new AppDbContext(options);

        _tempExtractFixture = Path.Combine(Path.GetTempPath(), $"extract-no-currency-{Guid.NewGuid():N}.json");
        File.WriteAllText(_tempExtractFixture, """
            {
              "schemaVersion": "v1",
              "supplierIdx": 0,
              "fields": {
                "product": { "value": "Bomba centrífuga", "sourceRefs": [] },
                "brand": { "value": "Pedrollo", "sourceRefs": [] },
                "totalAmount": { "value": "120000", "sourceRefs": [] }
              }
            }
            """);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
        if (File.Exists(_tempExtractFixture)) File.Delete(_tempExtractFixture);
    }

    [Test]
    public async Task GenerateAsync_ExtractMissingCurrencyCode_BackfilledFromSupplier_AndSucceeds()
    {
        var orchestrator = BuildOrchestrator(rateLimitCap: 100, extractFixture: _tempExtractFixture);
        var itemId = await SeedItemWithTwoSuppliersAsync();

        var result = await orchestrator.GenerateAsync(new GenerateComparisonCommand(
            ApplicationItemId: itemId,
            ActorUserId: "reviewer-1",
            ActorRole: "Reviewer",
            BypassRateLimit: false,
            BypassTokenCap: false), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<GenerateComparisonSuccess>(),
            "Schema-required currencyCode should be backfilled from the supplier assembly so the schema invariant holds.");

        var artifact = await _ctx.ComparisonArtifacts.FirstOrDefaultAsync(a => a.ApplicationItemId == itemId);
        Assert.That(artifact, Is.Not.Null);
    }

    [Test]
    public async Task GenerateAsync_RateLimitExceeded_PersistsFailureAuditWithoutCallerSaveChanges()
    {
        var orchestrator = BuildOrchestrator(rateLimitCap: 0, extractFixture: null);
        var itemId = await SeedItemWithTwoSuppliersAsync();

        var result = await orchestrator.GenerateAsync(new GenerateComparisonCommand(
            ApplicationItemId: itemId,
            ActorUserId: "reviewer-1",
            ActorRole: "Reviewer",
            BypassRateLimit: false,
            BypassTokenCap: false), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<GenerateComparisonFailure>());
        Assert.That(((GenerateComparisonFailure)result).FailureReason, Is.EqualTo("rate_limit_exceeded"));

        // The orchestrator must commit its own audit-row writes — the caller
        // (sync controller or worker post-orchestrator) does not save here.
        var auditCount = await _ctx.AdminAuditEvents
            .CountAsync(e => e.TargetId == itemId.ToString());
        Assert.That(auditCount, Is.GreaterThanOrEqualTo(1),
            "Failure audit must persist without the test fixture calling SaveChangesAsync.");
    }

    private ComparisonOrchestrator BuildOrchestrator(int rateLimitCap, string? extractFixture)
    {
        var fixturesRoot = ResolveFixturesRoot();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiComparison:Provider"] = "Stub",
            ["AiComparison:PromptVersion"] = "2026-05-11",
            ["AiComparison:SchemaVersion"] = "v1",
            ["AiComparison:Anthropic:ExtractModel"] = "claude-sonnet-4-6",
            ["AiComparison:Anthropic:CompareModel"] = "claude-opus-4-7",
            ["AiComparison:StubFixtures:Extract"] =
                extractFixture ?? Path.Combine(fixturesRoot, "canned-extract.json"),
            ["AiComparison:StubFixtures:Compare"] = Path.Combine(fixturesRoot, "canned-compare.json"),
            ["AiComparison:RateLimitPerApp24h"] = rateLimitCap.ToString(),
            ["AiComparison:TokenCapPerRunInput"] = "200000",
            ["AiComparison:ExtractConcurrency"] = "2",
        }).Build();

        var catalog = new PromptCatalog(config);
        var validator = new SchemaValidator(catalog);
        var redactor = new PiiRedactor();
        var stub = new StubAiClient(config);
        var assembler = new SupplierAssembler(_ctx);
        var artifactRepo = new ComparisonArtifactRepository(_ctx);
        var jobRepo = new ComparisonJobRepository(_ctx);
        var auditWriter = new AdminAuditWriter(_ctx);
        var unitOfWork = new UnitOfWork(_ctx);
        var rateLimitCounter = new AdminAuditRateLimitCounter(_ctx);
        var rateLimitGuard = new RateLimitGuard(rateLimitCounter, config, NullLogger<RateLimitGuard>.Instance);
        var tokenCapGuard = new TokenCapGuard(config, NullLogger<TokenCapGuard>.Instance);
        var auditFactory = new AdminAuditEventComparisonFactory();

        return new ComparisonOrchestrator(
            assembler, redactor, stub, catalog, validator,
            artifactRepo, jobRepo, rateLimitGuard, tokenCapGuard,
            auditFactory, auditWriter, unitOfWork,
            new InMemoryObjectStorage(),
            config, NullLogger<ComparisonOrchestrator>.Instance);
    }

    private async Task<int> SeedItemWithTwoSuppliersAsync()
    {
        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}",
            legalId: "1-1234-5678",
            firstName: "Daniel", lastName: "Centeno",
            email: $"a-{Guid.NewGuid():N}@example.com",
            phone: null, performanceScore: null);
        _ctx.Applicants.Add(applicant);

        var category = new Category("Equipment", "desc", isActive: true);
        _ctx.Categories.Add(category);
        await _ctx.SaveChangesAsync();

        var doc1 = new Document("quote-A.pdf", "/store/A", 1024, "application/pdf");
        var doc2 = new Document("quote-B.pdf", "/store/B", 1024, "application/pdf");
        _ctx.Documents.AddRange(doc1, doc2);

        var supplierA = Supplier.CreateDraft(
            legalId: "3-101-0001", name: "Proveedor A", createdByApplicantId: applicant.Id,
            firstBranchName: "Sede 1", firstBranchContactName: null, firstBranchEmail: null,
            firstBranchPhone: null, firstBranchAddressLine: null, firstBranchProvince: null,
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(supplierA, SupplierVerificationStatus.Verified);
        var supplierB = Supplier.CreateDraft(
            legalId: "3-101-0002", name: "Proveedor B", createdByApplicantId: applicant.Id,
            firstBranchName: "Sede 2", firstBranchContactName: null, firstBranchEmail: null,
            firstBranchPhone: null, firstBranchAddressLine: null, firstBranchProvince: null,
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(supplierB, SupplierVerificationStatus.Verified);
        _ctx.Suppliers.AddRange(supplierA, supplierB);
        await _ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, 1, "Test Company");
        app.AssignPublicCode(Helpers.TestPublicCodes.Next());
        var item = new Item("Bomba centrífuga", category.Id);
        app.AddItem(item);
        _ctx.Applications.Add(app);
        await _ctx.SaveChangesAsync();

        item.AddQuotation(supplierA, supplierA.Branches.First(), doc1,
            price: 120000m,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            currency: "CRC");
        item.AddQuotation(supplierB, supplierB.Branches.First(), doc2,
            price: 165000m,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            currency: "CRC");
        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.UnderReview);
        await _ctx.SaveChangesAsync();

        return item.Id;
    }

    private static string ResolveFixturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Fixtures", "AiComparison");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/Fixtures/AiComparison not found.");
    }
}
