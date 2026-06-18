using FundingPlatform.Application.Suppliers.Compliance;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// Spec 038 (US1/US2/US3) — <see cref="SupplierComplianceService"/> persists the
/// regulatory edit and writes one <c>supplier.*</c> audit row per change, and the
/// "reviewed — no change" path refreshes freshness without altering the value.
/// EF InMemory does NOT enforce ROWVERSION, so these tests deliberately pass an
/// empty RowVersion (the last-write-wins branch). The optimistic-concurrency
/// conflict path (DbUpdateConcurrencyException → es-CR "recargue") is therefore
/// NOT exercised here; it requires a real SQL Server and is currently unverified
/// by an automated test (tracked in EVOLUTION.md §D-E).
/// </summary>
[TestFixture]
public class SupplierComplianceServiceTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<int> SeedSupplierAsync(string dbName)
    {
        using var ctx = CreateContext(dbName);
        var s = Supplier.CreateDraft("3-101-700001", "Proveedor X", 1, "Sede principal",
            null, null, null, null, null, null, null);
        ctx.Suppliers.Add(s);
        await ctx.SaveChangesAsync();
        return s.Id;
    }

    [Test]
    public async Task EditComplianceAsync_PersistsStatusesAndPme_AndWritesAuditRows()
    {
        var dbName = $"sup-comp-edit-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAsync(dbName);

        using (var ctx = CreateContext(dbName))
        {
            var svc = new SupplierComplianceService(ctx, new AdminAuditEventWriter(ctx));
            var result = await svc.EditComplianceAsync(new EditSupplierComplianceCommand(
                supplierId, "Proveedor X",
                HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
                IsPmeOrPyme: true, HasWarning: false, WarningNote: null,
                ActorUserId: "auditor-1", RowVersion: System.Array.Empty<byte>()), CancellationToken.None);

            Assert.That(result.Ok, Is.True, result.ErrorEsCr);
        }

        using (var ctx = CreateContext(dbName))
        {
            var s = await ctx.Suppliers.FirstAsync(x => x.Id == supplierId);
            Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia));
            Assert.That(s.CcssStatus, Is.EqualTo(CcssStatus.AlDia));
            Assert.That(s.SicopStatus, Is.EqualTo(SicopStatus.SinSanciones));
            Assert.That(s.IsPmeOrPyme, Is.True);
            Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("auditor-1"));

            var audits = await ctx.AdminAuditEvents.ToListAsync();
            // 3 regulatory_changed + 1 pme_changed.
            Assert.That(audits.Count(a => a.Action == AdminAuditEvent.SupplierRegulatoryChanged), Is.EqualTo(3));
            Assert.That(audits.Count(a => a.Action == AdminAuditEvent.SupplierPmeChanged), Is.EqualTo(1));
            Assert.That(audits.All(a => a.TargetType == AdminAuditEvent.TargetTypeSupplier), Is.True);
            Assert.That(audits.All(a => a.TargetId == supplierId.ToString()), Is.True,
                "supplier.* events carry the real supplier id as TargetId");

            // FR-012 — the payload captures field, old/new value, source, kind.
            var haciendaRow = audits.Single(a =>
                a.Action == AdminAuditEvent.SupplierRegulatoryChanged
                && a.PayloadJson != null
                && a.PayloadJson.Contains("\"Hacienda\""));
            using var doc = System.Text.Json.JsonDocument.Parse(haciendaRow.PayloadJson!);
            var root = doc.RootElement;
            Assert.That(root.GetProperty("field").GetString(), Is.EqualTo("Hacienda"));
            Assert.That(root.GetProperty("oldValue").ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Null));
            Assert.That(root.GetProperty("newValue").GetString(), Is.EqualTo("2"));
            Assert.That(root.GetProperty("source").GetString(), Is.EqualTo("Manual"));
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("Changed"));
            Assert.That(root.GetProperty("supplierId").GetInt32(), Is.EqualTo(supplierId));
        }
    }

    [Test]
    public async Task EditComplianceAsync_WarningNoteTooLong_ReturnsEsCrError()
    {
        var dbName = $"sup-comp-warnlen-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAsync(dbName);

        using var ctx = CreateContext(dbName);
        var svc = new SupplierComplianceService(ctx, new AdminAuditEventWriter(ctx));
        var result = await svc.EditComplianceAsync(new EditSupplierComplianceCommand(
            supplierId, "Proveedor X", null, null, null,
            IsPmeOrPyme: false, HasWarning: true, WarningNote: new string('x', 1001),
            ActorUserId: "auditor-1", RowVersion: System.Array.Empty<byte>()), CancellationToken.None);

        Assert.That(result.Ok, Is.False);
        Assert.That(result.ErrorEsCr, Does.Contain("1000"));
    }

    [Test]
    public async Task EditComplianceAsync_WarningOff_ClearsNote_AndAudits()
    {
        var dbName = $"sup-comp-warn-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAsync(dbName);

        using (var ctx = CreateContext(dbName))
        {
            var svc = new SupplierComplianceService(ctx, new AdminAuditEventWriter(ctx));
            await svc.EditComplianceAsync(new EditSupplierComplianceCommand(
                supplierId, "Proveedor X", null, null, null,
                IsPmeOrPyme: false, HasWarning: true, WarningNote: "revisar contrato",
                ActorUserId: "auditor-1", RowVersion: System.Array.Empty<byte>()), CancellationToken.None);
        }

        using (var ctx = CreateContext(dbName))
        {
            var svc = new SupplierComplianceService(ctx, new AdminAuditEventWriter(ctx));
            await svc.EditComplianceAsync(new EditSupplierComplianceCommand(
                supplierId, "Proveedor X", null, null, null,
                IsPmeOrPyme: false, HasWarning: false, WarningNote: "ignored",
                ActorUserId: "auditor-1", RowVersion: System.Array.Empty<byte>()), CancellationToken.None);
        }

        using (var ctx = CreateContext(dbName))
        {
            var s = await ctx.Suppliers.FirstAsync(x => x.Id == supplierId);
            Assert.That(s.HasWarning, Is.False);
            Assert.That(s.WarningNote, Is.Null);
            var warnAudits = await ctx.AdminAuditEvents
                .CountAsync(a => a.Action == AdminAuditEvent.SupplierWarningChanged);
            Assert.That(warnAudits, Is.EqualTo(2), "set + clear each audited");
        }
    }

    [Test]
    public async Task ConfirmReviewedAsync_RefreshesTimestamp_WritesReviewedAudit()
    {
        var dbName = $"sup-comp-confirm-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAsync(dbName);

        // First set a Hacienda value (so the field can be re-confirmed).
        using (var ctx = CreateContext(dbName))
        {
            var svc = new SupplierComplianceService(ctx, new AdminAuditEventWriter(ctx));
            await svc.EditComplianceAsync(new EditSupplierComplianceCommand(
                supplierId, "Proveedor X", HaciendaStatus.AlDia, null, null,
                false, false, null, "auditor-1", System.Array.Empty<byte>()), CancellationToken.None);
        }

        DateTime before;
        using (var ctx = CreateContext(dbName))
        {
            before = (await ctx.Suppliers.FirstAsync(x => x.Id == supplierId)).HaciendaLastReviewedAt!.Value;
        }

        using (var ctx = CreateContext(dbName))
        {
            var svc = new SupplierComplianceService(ctx, new AdminAuditEventWriter(ctx));
            var result = await svc.ConfirmReviewedAsync(
                supplierId, RegulatoryField.Hacienda, "auditor-2",
                System.Array.Empty<byte>(), CancellationToken.None);
            Assert.That(result.Ok, Is.True, result.ErrorEsCr);
        }

        using (var ctx = CreateContext(dbName))
        {
            var s = await ctx.Suppliers.FirstAsync(x => x.Id == supplierId);
            Assert.That(s.HaciendaStatus, Is.EqualTo(HaciendaStatus.AlDia), "value unchanged");
            Assert.That(s.HaciendaLastReviewedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(s.HaciendaLastReviewedBy, Is.EqualTo("auditor-2"));
            var reviewedAudits = await ctx.AdminAuditEvents
                .CountAsync(a => a.Action == AdminAuditEvent.SupplierRegulatoryReviewed);
            Assert.That(reviewedAudits, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ConfirmReviewedAsync_UnsetStatus_ReturnsEsCrError()
    {
        var dbName = $"sup-comp-confirm-unset-{Guid.NewGuid():N}";
        var supplierId = await SeedSupplierAsync(dbName);

        using var ctx = CreateContext(dbName);
        var svc = new SupplierComplianceService(ctx, new AdminAuditEventWriter(ctx));
        var result = await svc.ConfirmReviewedAsync(
            supplierId, RegulatoryField.Ccss, "auditor-1",
            System.Array.Empty<byte>(), CancellationToken.None);

        Assert.That(result.Ok, Is.False);
        Assert.That(result.ErrorEsCr, Does.Contain("Defina un estado"));
    }
}
