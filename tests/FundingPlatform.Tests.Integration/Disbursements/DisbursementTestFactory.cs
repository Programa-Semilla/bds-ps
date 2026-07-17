using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using FundingPlatform.Tests.Integration.AiComparison;
using FundingPlatform.Tests.Integration.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 045 — shared InMemory harness for the disbursement service/projection/audit
/// integration tests (spec-036 precedent: InMemory here, real-SQL enum materialization
/// proven by the E2E suite). Builds an executed application, optionally with a selected-supplier
/// CRC quotation so <c>ApplicationCurrencyTotal.Compute</c> returns a known total.
/// </summary>
internal static class DisbursementTestFactory
{
    public const string Actor = "finop-1";

    public static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    public static DisbursementService NewService(AppDbContext ctx, InMemoryObjectStorage storage) =>
        new(ctx, storage, new AdminAuditEventWriter(ctx),
            new Reconciliation.NoOpReconciliationMaterializer(), NullLogger<DisbursementService>.Instance);

    public static ParticipantBalanceProjection NewProjection(AppDbContext ctx) => new(ctx);

    /// <summary>Seeds an executed application. When <paramref name="crcQuotation"/> is set,
    /// attaches a single selected-supplier CRC quotation of that amount so
    /// <c>ApplicationCurrencyTotal.Compute(app).Total</c> equals it.</summary>
    public static async Task<int> SeedExecutedAppAsync(AppDbContext ctx, decimal? crcQuotation = null)
    {
        if (!await ctx.Users.AnyAsync(u => u.Id == Actor))
        {
            ctx.Users.Add(new ApplicationUser
            {
                Id = Actor, UserName = "finop", Email = "finop@x.test", FirstName = "Fin", LastName = "Op",
            });
        }

        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}", legalId: "L-1", firstName: "Ana", lastName: "P",
            email: "ana@example.com", phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, groupId: 1, null, companyName: "Empresa");
        app.AssignPublicCode(TestPublicCodes.Next());

        if (crcQuotation is { } price)
        {
            var category = new Category("Equipment", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var supplier = Supplier.CreateDraft(
                legalId: "S-1", name: "Supplier 1", createdByApplicantId: applicant.Id,
                firstBranchName: "Sede principal", firstBranchContactName: null, firstBranchEmail: null,
                firstBranchPhone: null, firstBranchAddressLine: null, firstBranchProvince: "San Jose",
                firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
            ctx.Suppliers.Add(supplier);
            await ctx.SaveChangesAsync();

            var doc = new Document("crc.pdf", "key-crc", 1L, "application/pdf");
            ctx.Documents.Add(doc);
            await ctx.SaveChangesAsync();

            var item = new Item("Item", category.Id);
            app.AddItem(item);
            ctx.Applications.Add(app);
            await ctx.SaveChangesAsync();

            item.AddQuotation(
                supplier, supplier.Branches.First(), doc,
                price: price,
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                currency: "CRC",
                deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
                warranty: new TimeDuration(12, DurationUnit.Months));
            item.Approve(supplier.Id, "ok");
            await ctx.SaveChangesAsync();
        }
        else
        {
            ctx.Applications.Add(app);
            await ctx.SaveChangesAsync();
        }

        typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, ApplicationState.AgreementExecuted);
        await ctx.SaveChangesAsync();
        return app.Id;
    }

    /// <summary>Pre-seeds a known Allocation ledger entry so the balance ceiling is deterministic
    /// without needing a quotation aggregate.</summary>
    public static async Task SeedAllocationAsync(AppDbContext ctx, int appId, decimal amount)
    {
        ctx.DisbursementLedgerEntries.Add(DisbursementLedgerEntry.Allocation(appId, amount, Actor));
        await ctx.SaveChangesAsync();
    }

    public static Stream Pdf() => new MemoryStream("%PDF-1.4 body"u8.ToArray());
}
