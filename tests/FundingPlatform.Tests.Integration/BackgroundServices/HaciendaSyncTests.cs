using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Abstractions.Hacienda;
using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.BackgroundServices;
using FundingPlatform.Infrastructure.Hacienda;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Tests.Integration.BackgroundServices;

/// <summary>
/// Spec 043 / US2 (T025) — DB-backed coverage of <see cref="HaciendaSyncService.RunOnceAsync"/>
/// driven by <see cref="FakeHaciendaApiClient"/> (the live API is never called). Covers
/// changed/unchanged/404/no-inscrito outcomes + the audit verbs (source Api). The
/// RowVersion-concurrency skip is real-SQL-only (covered by E2E).
///
/// SCOPE: EF InMemory provider (mirrors the rest of this project's service tests).
/// </summary>
[TestFixture]
public class HaciendaSyncTests
{
    [SetUp]
    public void ResetFake() => FakeHaciendaApiClient.Reset();

    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<IHaciendaApiClient, FakeHaciendaApiClient>();
        services.AddScoped<IAdminAuditEventWriter, AdminAuditEventWriter>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static HaciendaSyncService NewService(ServiceProvider sp) =>
        new(sp, Options.Create(new HaciendaSyncOptions { Provider = "Fake", BatchSize = 50 }),
            NullLogger<HaciendaSyncService>.Instance);

    private static async Task<int> SeedSupplierAsync(ServiceProvider sp, Action<Supplier>? configure = null)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = Supplier.CreateDraft(
            $"3-101-{Random.Shared.Next(100000, 999999)}", "Proveedor X", 1, "Sede principal",
            null, null, null, null, null, null, null);
        configure?.Invoke(supplier);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private static async Task<Supplier> LoadAsync(ServiceProvider sp, int supplierId)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.FirstAsync(s => s.Id == supplierId);
    }

    private static async Task<List<AdminAuditEvent>> AuditsAsync(ServiceProvider sp, int supplierId)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AdminAuditEvents.Where(a => a.TargetId == supplierId.ToString()).ToListAsync();
    }

    [Test]
    public async Task ChangedValue_UpdatesStatus_WritesRegulatoryChanged_SourceApi()
    {
        using var sp = BuildProvider($"hac-changed-{Guid.NewGuid():N}");
        var id = await SeedSupplierAsync(sp); // Hacienda null → will change to AlDia
        FakeHaciendaApiClient.StageDefault(
            HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", false, false)));

        var summary = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(summary.Changed, Is.EqualTo(1));
        var s = await LoadAsync(sp, id);
        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
        Assert.That(s.HaciendaLastReviewedSource, Is.EqualTo(RegulatoryReviewSource.Api));
        Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("system"));
        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Success));

        var audits = await AuditsAsync(sp, id);
        Assert.That(audits.Any(a => a.Action == AdminAuditEvent.SupplierRegulatoryChanged), Is.True);
        Assert.That(audits.All(a => a.TargetType == AdminAuditEvent.TargetTypeSupplier), Is.True);
    }

    [Test]
    public async Task UnchangedValue_WritesRegulatoryReviewed_AndRefreshesTimestamp()
    {
        using var sp = BuildProvider($"hac-unchanged-{Guid.NewGuid():N}");
        var oldStamp = DateTime.UtcNow.AddDays(-40);
        var id = await SeedSupplierAsync(sp, s =>
            s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, null, false, false, null, "auditor-1", oldStamp));
        FakeHaciendaApiClient.StageDefault(
            HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", false, false)));

        var summary = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(summary.Unchanged, Is.EqualTo(1));
        var s = await LoadAsync(sp, id);
        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
        Assert.That(s.HaciendaLastReviewedAt, Is.GreaterThan(oldStamp), "unchanged sync refreshes freshness");
        Assert.That(s.HaciendaLastReviewedSource, Is.EqualTo(RegulatoryReviewSource.Api));

        var audits = await AuditsAsync(sp, id);
        Assert.That(audits.Any(a => a.Action == AdminAuditEvent.SupplierRegulatoryReviewed), Is.True);
    }

    [Test]
    public async Task Http404_MapsToSinInformacion()
    {
        using var sp = BuildProvider($"hac-404-{Guid.NewGuid():N}");
        var id = await SeedSupplierAsync(sp);
        FakeHaciendaApiClient.StageDefault(HaciendaLookupResult.NotRegistered());

        await NewService(sp).RunOnceAsync(CancellationToken.None);

        var s = await LoadAsync(sp, id);
        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.SinInformacion));
        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Success));
    }

    [Test]
    public async Task NoInscrito200_MapsToSinInscripcion()
    {
        using var sp = BuildProvider($"hac-noinsc-{Guid.NewGuid():N}");
        var id = await SeedSupplierAsync(sp);
        FakeHaciendaApiClient.StageDefault(
            HaciendaLookupResult.Found(null, new HaciendaSituacion("No inscrito", false, false)));

        await NewService(sp).RunOnceAsync(CancellationToken.None);

        var s = await LoadAsync(sp, id);
        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.SinInscripcion));
    }
}
