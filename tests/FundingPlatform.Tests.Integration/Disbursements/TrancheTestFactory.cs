using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using FundingPlatform.Tests.Integration.Helpers;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Disbursements;

/// <summary>
/// Spec 046 — shared InMemory harness for the tranche + budget-line commit/attribution tests
/// (spec-045/036 precedent: InMemory here, real-SQL enum materialization + unique-index races
/// proven by the E2E suite). Seeds an application with N priced, selected-supplier line items so
/// <c>ApplicationCurrencyTotal.LineBudget</c> returns a known budget per line.
/// </summary>
internal static class TrancheTestFactory
{
    public const string Actor = "reviewer-1";

    public static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    public static TrancheService NewTrancheService(AppDbContext ctx) =>
        new(ctx, new AdminAuditEventWriter(ctx));

    public static ParticipantBalanceProjection NewProjection(AppDbContext ctx) => new(ctx);

    /// <summary>Seeds an application with one selected-supplier CRC line item per price in
    /// <paramref name="prices"/>. State defaults to ResponseFinalized (tranches editable). Returns
    /// (applicationId, ordered itemIds).</summary>
    public static async Task<(int AppId, IReadOnlyList<int> ItemIds)> SeedAppWithPricedItemsAsync(
        AppDbContext ctx, decimal[] prices, ApplicationState state = ApplicationState.ResponseFinalized)
    {
        if (!await ctx.Users.AnyAsync(u => u.Id == Actor))
        {
            ctx.Users.Add(new ApplicationUser { Id = Actor, UserName = "rev", Email = "rev@x.test", FirstName = "Re", LastName = "V" });
        }

        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}", legalId: "L-1", firstName: "Ana", lastName: "P",
            email: "ana@example.com", phone: null, performanceScore: null);
        ctx.Applicants.Add(applicant);

        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        var supplier = Supplier.CreateDraft(
            legalId: "S-1", name: "Supplier 1", createdByApplicantId: applicant.Id,
            firstBranchName: "Sede principal", firstBranchContactName: null, firstBranchEmail: null,
            firstBranchPhone: null, firstBranchAddressLine: null, firstBranchProvince: "San Jose",
            firstBranchShippingDetails: null, firstBranchWarrantyInfo: null);
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, groupId: 1, null, companyName: "Empresa");
        app.AssignPublicCode(TestPublicCodes.Next());
        var items = new List<Item>();
        foreach (var _ in prices)
        {
            var item = new Item("Item", category.Id);
            app.AddItem(item);
            items.Add(item);
        }
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        for (var i = 0; i < prices.Length; i++)
        {
            var doc = new Document($"crc-{i}.pdf", $"key-crc-{i}", 1L, "application/pdf");
            ctx.Documents.Add(doc);
            await ctx.SaveChangesAsync();

            items[i].AddQuotation(
                supplier, supplier.Branches.First(), doc,
                price: prices[i],
                validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                currency: "CRC",
                deliveryLeadTime: new TimeDuration(30, DurationUnit.Days),
                warranty: new TimeDuration(12, DurationUnit.Months));
            items[i].Approve(supplier.Id, "ok"); // sets SelectedSupplierId → LineBudget resolves
        }
        await ctx.SaveChangesAsync();

        typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, state);
        await ctx.SaveChangesAsync();

        return (app.Id, items.Select(i => i.Id).ToList());
    }

    /// <summary>Forces the application into a terminal executed state (freezes tranche structure).</summary>
    public static async Task ExecuteAsync(AppDbContext ctx, int appId)
    {
        var app = await ctx.Applications.SingleAsync(a => a.Id == appId);
        typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, ApplicationState.AgreementExecuted);
        await ctx.SaveChangesAsync();
    }
}
