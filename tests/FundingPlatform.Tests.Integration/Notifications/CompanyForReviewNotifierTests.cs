using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Integration.Notifications;

/// <summary>
/// Spec 041 / US4 / T034 / FR-013 — the "nueva empresa para revisión" stub renders
/// in the brand shell with a populated "Detalle de la empresa" card, AND no live
/// trigger/call site is wired (deferred to OQ-1). The real Razor render is asserted
/// by the design-system render path; here a capturing renderer proves the notifier
/// builds the branded model with the company detail.
/// </summary>
[TestFixture]
public class CompanyForReviewNotifierTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:BaseUrl"] = "https://test.example" })
            .Build();

    [Test]
    public async Task NotifyAsync_renders_branded_model_with_company_detail_card()
    {
        var dbName = $"company-notif-{Guid.NewGuid():N}";
        int companyId;
        using (var seed = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: "u-1", legalId: "3-101-555666",
                firstName: "Marta", lastName: "Empresaria",
                email: "marta@programa-semilla.test", phone: null, performanceScore: null);
            seed.Applicants.Add(applicant);
            await seed.SaveChangesAsync();

            var company = new Company(applicant.Id, "Cooperativa Verde R.L.");
            seed.Companies.Add(company);
            await seed.SaveChangesAsync();
            companyId = company.Id;
        }

        var renderer = new DumpRenderer();
        using var ctx = CreateContext(dbName);
        var notifier = new CompanyForReviewNotifier(
            ctx, renderer, Config(), NullLogger<CompanyForReviewNotifier>.Instance);

        await notifier.NotifyAsync(companyId, CancellationToken.None);

        Assert.That(renderer.LastModel, Is.Not.Null, "the branded template must be rendered.");
        var m = renderer.LastModel!;
        Assert.Multiple(() =>
        {
            Assert.That(m.HeroTitle, Is.EqualTo("Nueva empresa para revisión"));
            Assert.That(m.Subject, Does.Contain("Cooperativa Verde R.L."));
            Assert.That(m.CtaUrl, Is.Null, "FR-005: review route deferred (OQ-1) — no CTA invented.");
            Assert.That(m.CardRows, Is.Not.Null);
            var values = string.Join(" | ", m.CardRows!.Select(r => $"{r.Label}: {r.Value}"));
            Assert.That(values, Does.Contain("Cooperativa Verde R.L."), "company name in the Detalle card.");
            Assert.That(values, Does.Contain("3-101-555666"), "applicant identificación in the Detalle card.");
            Assert.That(values, Does.Contain("Marta Empresaria"), "applicant name in the Detalle card.");
        });
    }

    [Test]
    public async Task NotifyAsync_unknown_company_is_a_noop()
    {
        using var ctx = CreateContext($"company-notif-none-{Guid.NewGuid():N}");
        var renderer = new DumpRenderer();
        var notifier = new CompanyForReviewNotifier(
            ctx, renderer, Config(), NullLogger<CompanyForReviewNotifier>.Instance);

        Assert.DoesNotThrowAsync(() => notifier.NotifyAsync(999, CancellationToken.None));
        Assert.That(renderer.LastModel, Is.Null, "no render for an unknown company.");
    }

    [Test]
    public void No_live_trigger_or_call_site_is_wired_for_the_company_notifier()
    {
        // FR-013 / OQ-1 — the seam + template exist but NOTHING triggers them yet.
        // Assert the only production references to ICompanyForReviewNotifier are the
        // interface declaration, the impl, and the DI registration — no controller /
        // service / handler invokes NotifyAsync.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FundingPlatform.slnx")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "solution root not found.");

        var srcRoot = Path.Combine(dir!.FullName, "src");
        var referencingFiles = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("ICompanyForReviewNotifier", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToList();

        Assert.That(referencingFiles, Is.EquivalentTo(new[]
        {
            "CompanyForReviewNotifier.cs",       // Infrastructure impl
            "DependencyInjection.cs",            // DI registration
            "ICompanyForReviewNotifier.cs",      // Application interface
        }), "OQ-1: no live trigger/call site may reference the company notifier yet.");
    }

    /// <summary>Captures the last <see cref="DirectEmailModel"/> the notifier rendered.</summary>
    private sealed class DumpRenderer : IEmailViewRenderer
    {
        public DirectEmailModel? LastModel { get; private set; }
        public Task<string> RenderViewAsync(string viewPath, object model, bool disableLayout, CancellationToken ct)
        {
            LastModel = (DirectEmailModel)model;
            return Task.FromResult("rendered");
        }
    }
}
