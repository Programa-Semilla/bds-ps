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
/// changed/unchanged/404/no-inscrito outcomes + the audit verbs (source Api).
///
/// NOT covered here: the RowVersion optimistic-concurrency skip (FR-025). EF InMemory does
/// not enforce row-version concurrency, and no test deterministically races a concurrent
/// auditor edit mid-sync — that path is verified by construction only (see review-findings T-1).
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
        var sp = services.BuildServiceProvider();

        // The sync attributes its audit + LastReviewedBy to the system sentinel (FK to AspNetUsers).
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(ApplicationUser.CreateSentinel("system@local"));
            db.SaveChanges();
        }
        return sp;
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
        // Attributed to the system sentinel (a real AspNetUsers id), not the literal "system".
        Assert.That(s.HaciendaLastReviewedBy, Is.Not.Null.And.Not.Empty);
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

    // ----- US3 — failures visible, never silent, never corrupting data -----

    [Test]
    public async Task ApiFailure_LeavesRegulatoryDataIntact_RecordsFailure_AndAudit()
    {
        using var sp = BuildProvider($"hac-fail-{Guid.NewGuid():N}");
        var reviewedAt = DateTime.UtcNow.AddDays(-3);
        var id = await SeedSupplierAsync(sp, s =>
            s.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, null, false, false, null, "auditor-1", reviewedAt));
        FakeHaciendaApiClient.StageDefault(HaciendaLookupResult.Failed("error simulado"));

        var summary = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(summary.Failed, Is.EqualTo(1));
        var s = await LoadAsync(sp, id);
        // FR-018 — status + last-reviewed are untouched.
        Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
        Assert.That(s.HaciendaLastReviewedAt, Is.EqualTo(reviewedAt));
        Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("auditor-1"));
        // Failure metadata recorded.
        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Failure));
        Assert.That(s.HaciendaSyncError, Is.EqualTo("error simulado"));

        var audits = await AuditsAsync(sp, id);
        Assert.That(audits.Any(a => a.Action == AdminAuditEvent.SupplierHaciendaSyncFailed), Is.True);
    }

    [Test]
    public async Task MalformedIdentification_RecordsFailure_WithoutCallingApi()
    {
        using var sp = BuildProvider($"hac-malformed-{Guid.NewGuid():N}");
        int id;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = Supplier.CreateDraft(
                "PASS123", "Proveedor pasaporte", 1, "Sede", null, null, null, null, null, null, null);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            id = supplier.Id;
        }
        FakeHaciendaApiClient.StageDefault(
            HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", false, false)));

        var summary = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(summary.Failed, Is.EqualTo(1));
        Assert.That(FakeHaciendaApiClient.LookupCallCount, Is.EqualTo(0), "malformed id must skip the API call");
        var s = await LoadAsync(sp, id);
        Assert.That(s.HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Failure));
        Assert.That(s.HaciendaStatus, Is.Null, "no status was set");
    }

    [Test]
    public async Task BatchSize_ProcessesAllSuppliersAcrossMultipleBatches()
    {
        using var sp = BuildProvider($"hac-batches-{Guid.NewGuid():N}");
        for (var i = 0; i < 5; i++) await SeedSupplierAsync(sp);
        FakeHaciendaApiClient.StageDefault(
            HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", false, false)));

        // BatchSize 2 with 5 suppliers → 3 batches; all must be processed (FR-017).
        var svc = new HaciendaSyncService(
            sp, Options.Create(new HaciendaSyncOptions { Provider = "Fake", BatchSize = 2 }),
            NullLogger<HaciendaSyncService>.Instance);
        var summary = await svc.RunOnceAsync(CancellationToken.None);

        Assert.That(summary.Checked, Is.EqualTo(5));
        Assert.That(summary.Changed, Is.EqualTo(5));
    }

    [Test]
    public async Task BatchContinuesPastFailure_OtherProvidersStillSynced()
    {
        using var sp = BuildProvider($"hac-batch-{Guid.NewGuid():N}");
        int failId, okId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fail = Supplier.CreateDraft("3-101-111111", "Falla", 1, "Sede", null, null, null, null, null, null, null);
            var ok = Supplier.CreateDraft("3-101-222222", "OK", 1, "Sede", null, null, null, null, null, null, null);
            db.Suppliers.AddRange(fail, ok);
            await db.SaveChangesAsync();
            failId = fail.Id; okId = ok.Id;
        }
        FakeHaciendaApiClient.StageOutcome("3101111111", HaciendaLookupResult.Failed("error simulado"));
        FakeHaciendaApiClient.StageDefault(
            HaciendaLookupResult.Found(null, new HaciendaSituacion("Inscrito", false, false)));

        var summary = await NewService(sp).RunOnceAsync(CancellationToken.None);

        Assert.That(summary.Failed, Is.EqualTo(1));
        Assert.That(summary.Changed, Is.EqualTo(1));
        Assert.That((await LoadAsync(sp, failId)).HaciendaSyncOutcome, Is.EqualTo(HaciendaSyncOutcome.Failure));
        Assert.That((await LoadAsync(sp, okId)).HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
    }
}
