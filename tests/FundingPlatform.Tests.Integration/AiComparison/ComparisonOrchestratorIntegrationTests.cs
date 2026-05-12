using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using FundingPlatform.Application.Audit;
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

[TestFixture]
public class ComparisonOrchestratorIntegrationTests
{
    private AppDbContext _ctx = null!;
    private ComparisonOrchestrator _orchestrator = null!;
    private IConfiguration _config = null!;
    private StubAiClient _stub = null!;

    [SetUp]
    public void Setup()
    {
        StubAiClient.ResetCallCounters();

        var dbName = $"orch-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _ctx = new AppDbContext(options);

        var fixturesRoot = ResolveFixturesRoot();
        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiComparison:Provider"] = "Stub",
            ["AiComparison:PromptVersion"] = "2026-05-11",
            ["AiComparison:SchemaVersion"] = "v1",
            ["AiComparison:Anthropic:ExtractModel"] = "claude-sonnet-4-6",
            ["AiComparison:Anthropic:CompareModel"] = "claude-opus-4-7",
            ["AiComparison:StubFixtures:Extract"] = Path.Combine(fixturesRoot, "canned-extract.json"),
            ["AiComparison:StubFixtures:Compare"] = Path.Combine(fixturesRoot, "canned-compare.json"),
            ["AiComparison:RateLimitPerApp24h"] = "100",
            ["AiComparison:TokenCapPerRunInput"] = "200000",
            ["AiComparison:ExtractConcurrency"] = "2",
        }).Build();

        var catalog = new PromptCatalog(_config);
        var validator = new SchemaValidator(catalog);
        var redactor = new PiiRedactor();
        _stub = new StubAiClient(_config);
        var assembler = new SupplierAssembler(_ctx);
        var artifactRepo = new ComparisonArtifactRepository(_ctx);
        var jobRepo = new ComparisonJobRepository(_ctx);
        var auditWriter = new AdminAuditWriter(_ctx);
        var rateLimitCounter = new AdminAuditRateLimitCounter(_ctx);
        var rateLimitGuard = new RateLimitGuard(rateLimitCounter, _config, NullLogger<RateLimitGuard>.Instance);
        var tokenCapGuard = new TokenCapGuard(_config, NullLogger<TokenCapGuard>.Instance);
        var auditFactory = new AdminAuditEventComparisonFactory();

        _orchestrator = new ComparisonOrchestrator(
            assembler, redactor, _stub, catalog, validator,
            artifactRepo, jobRepo, rateLimitGuard, tokenCapGuard,
            auditFactory, auditWriter, _config, NullLogger<ComparisonOrchestrator>.Instance);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

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

        var app = new AppEntity(applicant.Id, "Test Company");
        var item = new Item("Bomba centrífuga", category.Id, "1HP, acero");
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

    [Test]
    public async Task GenerateAsync_HappyPath_PersistsArtifact_AndEmitsAudit()
    {
        var itemId = await SeedItemWithTwoSuppliersAsync();

        var result = await _orchestrator.GenerateAsync(new GenerateComparisonCommand(
            ApplicationItemId: itemId,
            ActorUserId: "reviewer-1",
            ActorRole: "Reviewer",
            BypassRateLimit: false,
            BypassTokenCap: false), CancellationToken.None);
        await _ctx.SaveChangesAsync();

        Assert.That(result, Is.InstanceOf<GenerateComparisonSuccess>());

        var artifact = await _ctx.ComparisonArtifacts.FirstOrDefaultAsync(a => a.ApplicationItemId == itemId);
        Assert.That(artifact, Is.Not.Null);
        Assert.That(artifact!.InputHash, Has.Length.EqualTo(64));
        Assert.That(artifact.SchemaVersion, Is.EqualTo("v1"));
        Assert.That(artifact.PromptVersion, Is.EqualTo("2026-05-11"));

        var audit = await _ctx.AdminAuditEvents.FirstOrDefaultAsync(e => e.Action == "AiComparisonGenerated");
        Assert.That(audit, Is.Not.Null);
        Assert.That(audit!.TargetId, Is.EqualTo(itemId.ToString()));
    }

    [Test]
    public async Task GenerateAsync_CachedFresh_ShortCircuits_NoAdditionalAiCalls()
    {
        var itemId = await SeedItemWithTwoSuppliersAsync();

        await _orchestrator.GenerateAsync(new GenerateComparisonCommand(
            itemId, "reviewer-1", "Reviewer", false, false), CancellationToken.None);
        await _ctx.SaveChangesAsync();
        var firstCalls = StubAiClient.CompareCallCount;

        await _orchestrator.GenerateAsync(new GenerateComparisonCommand(
            itemId, "reviewer-1", "Reviewer", false, false), CancellationToken.None);
        await _ctx.SaveChangesAsync();

        Assert.That(StubAiClient.CompareCallCount, Is.EqualTo(firstCalls),
            "Cached path should not trigger additional compare AI calls.");
    }

    [Test]
    public async Task GetCachedComparisonAsync_NoArtifact_ReturnsNull()
    {
        var itemId = await SeedItemWithTwoSuppliersAsync();
        var cached = await _orchestrator.GetCachedComparisonAsync(itemId, CancellationToken.None);
        Assert.That(cached, Is.Null);
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
