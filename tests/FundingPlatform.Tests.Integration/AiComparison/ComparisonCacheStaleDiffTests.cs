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

/// <summary>Spec 020 / US2 — cache + stale-detection diff over the live state.</summary>
[TestFixture]
public class ComparisonCacheStaleDiffTests
{
    private AppDbContext _ctx = null!;
    private ComparisonOrchestrator _orchestrator = null!;
    private IConfiguration _config = null!;

    [SetUp]
    public void Setup()
    {
        StubAiClient.ResetCallCounters();

        var dbName = $"stale-{Guid.NewGuid():N}";
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
        var stub = new StubAiClient(_config);
        var assembler = new SupplierAssembler(_ctx);
        var artifactRepo = new ComparisonArtifactRepository(_ctx);
        var jobRepo = new ComparisonJobRepository(_ctx);
        var auditWriter = new AdminAuditWriter(_ctx);
        var rateLimitCounter = new AdminAuditRateLimitCounter(_ctx);
        var rateLimitGuard = new RateLimitGuard(rateLimitCounter, _config, NullLogger<RateLimitGuard>.Instance);
        var tokenCapGuard = new TokenCapGuard(_config, NullLogger<TokenCapGuard>.Instance);
        var auditFactory = new AdminAuditEventComparisonFactory();

        _orchestrator = new ComparisonOrchestrator(
            assembler, redactor, stub, catalog, validator,
            artifactRepo, jobRepo, rateLimitGuard, tokenCapGuard,
            auditFactory, auditWriter, _config, NullLogger<ComparisonOrchestrator>.Instance);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task FreshArtifact_PrimedByGeneration_ReportsFresh()
    {
        var itemId = await SeedAsync();
        await _orchestrator.GenerateAsync(new GenerateComparisonCommand(itemId, "u", "Reviewer", false, false), CancellationToken.None);
        await _ctx.SaveChangesAsync();

        var cached = await _orchestrator.GetCachedComparisonAsync(itemId, CancellationToken.None);
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.Freshness, Is.EqualTo(Freshness.Fresh));
    }

    [Test]
    public async Task LineEdit_FlipsCacheToStale()
    {
        var itemId = await SeedAsync();
        await _orchestrator.GenerateAsync(new GenerateComparisonCommand(itemId, "u", "Reviewer", false, false), CancellationToken.None);
        await _ctx.SaveChangesAsync();

        // Mutate a quotation price → input hash drifts.
        var item = await _ctx.Items.Include(i => i.Quotations).FirstAsync(i => i.Id == itemId);
        var quotation = item.Quotations.First();
        typeof(Quotation).GetProperty(nameof(Quotation.Price))!
            .SetValue(quotation, 999999m);
        await _ctx.SaveChangesAsync();

        var cached = await _orchestrator.GetCachedComparisonAsync(itemId, CancellationToken.None);
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.Freshness, Is.EqualTo(Freshness.Stale));
        Assert.That(cached.ChangedInputs, Is.Not.Empty);
    }

    [Test]
    public async Task ForceRegenerate_OverwritesArtifact_AndClearsStale()
    {
        var itemId = await SeedAsync();
        await _orchestrator.GenerateAsync(new GenerateComparisonCommand(itemId, "u", "Reviewer", false, false), CancellationToken.None);
        await _ctx.SaveChangesAsync();

        var first = await _ctx.ComparisonArtifacts.AsNoTracking().FirstAsync(a => a.ApplicationItemId == itemId);

        // Drift the input and regenerate with forceRegenerate=true.
        var item = await _ctx.Items.Include(i => i.Quotations).FirstAsync(i => i.Id == itemId);
        typeof(Quotation).GetProperty(nameof(Quotation.Price))!
            .SetValue(item.Quotations.First(), 250000m);
        await _ctx.SaveChangesAsync();

        await _orchestrator.GenerateAsync(new GenerateComparisonCommand(
            itemId, "u", "Reviewer", false, false, ForceRegenerate: true), CancellationToken.None);
        await _ctx.SaveChangesAsync();

        var refreshed = await _ctx.ComparisonArtifacts.AsNoTracking().FirstAsync(a => a.ApplicationItemId == itemId);
        Assert.That(refreshed.InputHash, Is.Not.EqualTo(first.InputHash));

        var cached = await _orchestrator.GetCachedComparisonAsync(itemId, CancellationToken.None);
        Assert.That(cached!.Freshness, Is.EqualTo(Freshness.Fresh));
    }

    private async Task<int> SeedAsync()
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

        var doc1 = new Document("a.pdf", "/store/a", 1024, "application/pdf");
        var doc2 = new Document("b.pdf", "/store/b", 1024, "application/pdf");
        _ctx.Documents.AddRange(doc1, doc2);

        var sA = Supplier.CreateDraft(
            legalId: "3-101-0001", name: "Proveedor A", createdByApplicantId: applicant.Id,
            firstBranchName: "Sede 1", firstBranchContactName: null, firstBranchEmail: null,
            firstBranchPhone: null, firstBranchAddressLine: null, firstBranchProvince: null,
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        var sB = Supplier.CreateDraft(
            legalId: "3-101-0002", name: "Proveedor B", createdByApplicantId: applicant.Id,
            firstBranchName: "Sede 2", firstBranchContactName: null, firstBranchEmail: null,
            firstBranchPhone: null, firstBranchAddressLine: null, firstBranchProvince: null,
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(sA, SupplierVerificationStatus.Verified);
        typeof(Supplier).GetProperty("VerificationStatus")!.SetValue(sB, SupplierVerificationStatus.Verified);
        _ctx.Suppliers.AddRange(sA, sB);
        await _ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, "Co");
        var item = new Item("Item", category.Id, "specs");
        app.AddItem(item);
        _ctx.Applications.Add(app);
        await _ctx.SaveChangesAsync();

        item.AddQuotation(sA, sA.Branches.First(), doc1, 100000m,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "CRC");
        item.AddQuotation(sB, sB.Branches.First(), doc2, 150000m,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "CRC");
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
