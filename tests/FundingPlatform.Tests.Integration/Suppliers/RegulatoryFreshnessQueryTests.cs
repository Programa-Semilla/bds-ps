using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Suppliers;

/// <summary>
/// Spec 043 / Phase 2 (T007) — <see cref="RegulatoryFreshnessService"/> query over
/// an application's selected suppliers (research D2). Exercises selected-supplier
/// scoping, window math, never-reviewed fields, multi-supplier flatten, and the
/// all-fresh empty result.
///
/// SCOPE: parity with the existing slice-A/persistence integration tests — uses the
/// EF InMemory provider so the entity-graph query is exercised without the
/// AspireFixture; real-SQL behavior (TINYINT conversion, RowVersion) is covered by
/// the E2E suite.
/// </summary>
[TestFixture]
public class RegulatoryFreshnessQueryTests
{
    private const int Window = 30;

    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RegulatoryFreshnessService Service(AppDbContext ctx) =>
        new(ctx, Options.Create(new RegulatoryFreshnessOptions { FreshnessWindowDays = Window }));

    private static TimeDuration Lead => new(30, DurationUnit.Days);
    private static TimeDuration Warranty => new(12, DurationUnit.Months);

    private static async Task<(int applicantId, int categoryId)> SeedBaseAsync(AppDbContext ctx)
    {
        var applicant = new Applicant(
            userId: $"user-{Guid.NewGuid():N}", legalId: $"FR-{Guid.NewGuid():N}"[..12],
            firstName: "Fresh", lastName: "Test", email: "fresh@example.com",
            phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();
        return (applicant.Id, category.Id);
    }

    private static Supplier MakeSupplier(int applicantId, string tag) => Supplier.CreateDraft(
        legalId: $"S-{tag}-{Guid.NewGuid():N}"[..14], name: $"Proveedor {tag}",
        createdByApplicantId: applicantId, firstBranchName: "Sede principal",
        firstBranchContactName: null, firstBranchEmail: null, firstBranchPhone: null,
        firstBranchAddressLine: null, firstBranchProvince: "San Jose",
        firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);

    /// <summary>Attaches a quotation from supplier to the item so Approve(supplierId) is legal.</summary>
    private static async Task AttachQuoteAsync(AppDbContext ctx, Item item, Supplier supplier, string tag)
    {
        var doc = new Document($"{tag}.pdf", $"key-{tag}-{Guid.NewGuid():N}", 1L, "application/pdf");
        ctx.Documents.Add(doc);
        await ctx.SaveChangesAsync();
        item.AddQuotation(
            supplier, supplier.Branches.First(), doc,
            price: 1000m, validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            currency: "CRC", deliveryLeadTime: Lead, warranty: Warranty);
        await ctx.SaveChangesAsync();
    }

    [Test]
    public async Task NeverReviewedSupplier_ProducesFindingPerRequiredField()
    {
        var dbName = $"fresh-never-{Guid.NewGuid():N}";
        int appId;
        using (var ctx = CreateContext(dbName))
        {
            var (applicantId, categoryId) = await SeedBaseAsync(ctx);
            var app = new AppEntity(applicantId, 1, null, "Empresa");
            app.AssignPublicCode(Helpers.TestPublicCodes.Next());
            app.AddItem(new Item("Item1", categoryId));
            ctx.Applications.Add(app);
            var supplier = MakeSupplier(applicantId, "never");
            ctx.Suppliers.Add(supplier);
            await ctx.SaveChangesAsync();

            await AttachQuoteAsync(ctx, app.Items[0], supplier, "never");
            app.Items[0].Approve(supplier.Id, "ok"); // CCSS null = sin revisar, not a block
            await ctx.SaveChangesAsync();
            appId = app.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var findings = await Service(ctx).GetStaleFindingsForApplicationAsync(appId, CancellationToken.None);

            Assert.That(findings.Select(f => f.Field), Is.EquivalentTo(new[]
            {
                RegulatoryField.Hacienda, RegulatoryField.Ccss, RegulatoryField.Sicop,
            }));
            Assert.That(findings.All(f => f.LastReviewedAt is null), Is.True);
        }
    }

    [Test]
    public async Task AllFreshSupplier_EmptyResult()
    {
        var dbName = $"fresh-all-{Guid.NewGuid():N}";
        int appId;
        using (var ctx = CreateContext(dbName))
        {
            var (applicantId, categoryId) = await SeedBaseAsync(ctx);
            var app = new AppEntity(applicantId, 1, null, "Empresa");
            app.AssignPublicCode(Helpers.TestPublicCodes.Next());
            app.AddItem(new Item("Item1", categoryId));
            ctx.Applications.Add(app);
            var supplier = MakeSupplier(applicantId, "fresh");
            supplier.ApplyRegulatoryEdit(
                HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
                false, false, null, "auditor-1", DateTime.UtcNow.AddDays(-1));
            ctx.Suppliers.Add(supplier);
            await ctx.SaveChangesAsync();

            await AttachQuoteAsync(ctx, app.Items[0], supplier, "fresh");
            app.Items[0].Approve(supplier.Id, "ok");
            await ctx.SaveChangesAsync();
            appId = app.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var findings = await Service(ctx).GetStaleFindingsForApplicationAsync(appId, CancellationToken.None);
            Assert.That(findings, Is.Empty);
        }
    }

    [Test]
    public async Task OnlySelectedSuppliersConsidered_UnselectedQuoteExcluded()
    {
        var dbName = $"fresh-scope-{Guid.NewGuid():N}";
        int appId;
        using (var ctx = CreateContext(dbName))
        {
            var (applicantId, categoryId) = await SeedBaseAsync(ctx);
            var app = new AppEntity(applicantId, 1, null, "Empresa");
            app.AssignPublicCode(Helpers.TestPublicCodes.Next());
            app.AddItem(new Item("ItemSelected", categoryId));
            app.AddItem(new Item("ItemUnselected", categoryId));
            ctx.Applications.Add(app);

            // Selected supplier: CCSS+Sicop fresh, Hacienda never reviewed (1 finding).
            var selected = MakeSupplier(applicantId, "sel");
            selected.ApplyRegulatoryEdit(
                null, CcssStatus.AlDia, SicopStatus.SinSanciones,
                false, false, null, "auditor-1", DateTime.UtcNow.AddDays(-1));
            // Unselected supplier: everything stale — must NOT appear.
            var unselected = MakeSupplier(applicantId, "unsel");
            ctx.Suppliers.AddRange(selected, unselected);
            await ctx.SaveChangesAsync();

            await AttachQuoteAsync(ctx, app.Items[0], selected, "sel");
            await AttachQuoteAsync(ctx, app.Items[1], unselected, "unsel");
            app.Items[0].Approve(selected.Id, "ok"); // item 1 left unselected
            await ctx.SaveChangesAsync();
            appId = app.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var findings = await Service(ctx).GetStaleFindingsForApplicationAsync(appId, CancellationToken.None);

            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].Field, Is.EqualTo(RegulatoryField.Hacienda));
            Assert.That(findings[0].SupplierName, Is.EqualTo("Proveedor sel"));
        }
    }

    [Test]
    public async Task MultipleSelectedSuppliers_FindingsFlattened()
    {
        var dbName = $"fresh-multi-{Guid.NewGuid():N}";
        int appId;
        using (var ctx = CreateContext(dbName))
        {
            var (applicantId, categoryId) = await SeedBaseAsync(ctx);
            var app = new AppEntity(applicantId, 1, null, "Empresa");
            app.AssignPublicCode(Helpers.TestPublicCodes.Next());
            app.AddItem(new Item("ItemA", categoryId));
            app.AddItem(new Item("ItemB", categoryId));
            ctx.Applications.Add(app);

            // Supplier A: stale CCSS only (Hacienda+Sicop fresh).
            var a = MakeSupplier(applicantId, "A");
            a.ApplyRegulatoryEdit(HaciendaStatus.AlDia, null, SicopStatus.SinSanciones,
                false, false, null, "auditor-1", DateTime.UtcNow.AddDays(-1));
            a.ApplyRegulatoryEdit(HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
                false, false, null, "auditor-1", DateTime.UtcNow.AddDays(-90));
            // Supplier B: stale Sicop only.
            var b = MakeSupplier(applicantId, "B");
            b.ApplyRegulatoryEdit(HaciendaStatus.AlDia, CcssStatus.AlDia, null,
                false, false, null, "auditor-1", DateTime.UtcNow.AddDays(-1));
            b.ApplyRegulatoryEdit(HaciendaStatus.AlDia, CcssStatus.AlDia, SicopStatus.SinSanciones,
                false, false, null, "auditor-1", DateTime.UtcNow.AddDays(-90));
            ctx.Suppliers.AddRange(a, b);
            await ctx.SaveChangesAsync();

            await AttachQuoteAsync(ctx, app.Items[0], a, "A");
            await AttachQuoteAsync(ctx, app.Items[1], b, "B");
            app.Items[0].Approve(a.Id, "ok");
            app.Items[1].Approve(b.Id, "ok");
            await ctx.SaveChangesAsync();
            appId = app.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var findings = await Service(ctx).GetStaleFindingsForApplicationAsync(appId, CancellationToken.None);

            Assert.That(findings, Has.Count.EqualTo(2));
            Assert.That(findings.Any(f => f.SupplierName == "Proveedor A" && f.Field == RegulatoryField.Ccss));
            Assert.That(findings.Any(f => f.SupplierName == "Proveedor B" && f.Field == RegulatoryField.Sicop));
        }
    }
}
