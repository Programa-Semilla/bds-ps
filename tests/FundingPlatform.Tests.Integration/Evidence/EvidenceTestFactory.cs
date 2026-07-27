using FundingPlatform.Application.Evidence;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Audit;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Services;
using FundingPlatform.Tests.Integration.AiComparison;
using FundingPlatform.Tests.Integration.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Evidence;

/// <summary>
/// Spec 047 — shared InMemory harness for the evidence-graph service/closure integration tests
/// (spec-045/046 precedent: InMemory here, real-SQL enum/one-current/cascade behaviour proven by
/// the E2E suite). Builds an executed application with N budget-lines (Items).
/// </summary>
internal static class EvidenceTestFactory
{
    public const string Actor = "finop-1";

    public static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    public static EvidenceService NewService(AppDbContext ctx, InMemoryObjectStorage storage) =>
        new(ctx, storage, new AdminAuditEventWriter(ctx),
            new Reconciliation.NoOpReconciliationMaterializer(), NullLogger<EvidenceService>.Instance);

    /// <summary>Seeds an executed application with <paramref name="lineCount"/> budget-lines and
    /// returns (applicationId, itemIds).</summary>
    public static async Task<(int AppId, IReadOnlyList<int> ItemIds)> SeedExecutedAppWithLinesAsync(
        AppDbContext ctx, int lineCount)
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

        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, groupId: 1, null, companyName: "Empresa");
        app.AssignPublicCode(TestPublicCodes.Next());
        var items = new List<Item>();
        for (var i = 0; i < lineCount; i++)
        {
            var item = new Item($"Line {i + 1}", category.Id);
            app.AddItem(item);
            items.Add(item);
        }
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, ApplicationState.AgreementExecuted);
        await ctx.SaveChangesAsync();

        return (app.Id, items.Select(i => i.Id).ToList());
    }

    public static AttachEvidenceCommand AttachInvoice(
        int appId, decimal amount, IEnumerable<(int ItemId, decimal Amount)> lines, int? disbursementId = null,
        EvidenceType type = EvidenceType.Invoice)
        => new(
            appId, type, disbursementId, amount, "CRC", "F-001", new DateOnly(2026, 7, 16), null,
            lines.Select(l => new EvidenceLineAllocationInput(l.ItemId, l.Amount)).ToList(),
            Pdf(), "invoice.pdf", "application/pdf", 1024);

    public static DocumentRuleService NewDocRuleService(AppDbContext ctx) =>
        new(ctx, new AdminAuditEventWriter(ctx));

    public static LineCompletenessProjection NewCompletenessProjection(AppDbContext ctx) =>
        new(ctx, NewDocRuleService(ctx));

    /// <summary>Seeds a global-default DocumentRuleSet (CategoryId null) requiring the given types.</summary>
    public static async Task SeedGlobalDefaultAsync(AppDbContext ctx, params EvidenceType[] requiredTypes)
    {
        var set = DocumentRuleSet.Create(null);
        set.ReplaceItems(requiredTypes.Select(t => (t, true)));
        ctx.DocumentRuleSets.Add(set);
        await ctx.SaveChangesAsync();
    }

    /// <summary>Seeds a VALIDATED disbursement paying <paramref name="itemId"/> with an Invoice
    /// evidence, so the completeness projection sees an Invoice present for that line (D1 both-source).</summary>
    public static async Task SeedValidatedDisbursementWithInvoiceAsync(AppDbContext ctx, int appId, int itemId, decimal amount)
    {
        var app = await ctx.Applications.FirstAsync(a => a.Id == appId);
        var d = Domain.Entities.Disbursement.Record(app, Actor, new DateOnly(2026, 7, 16), amount, "TX-1", null);
        ctx.Disbursements.Add(d);
        await ctx.SaveChangesAsync();

        var evidence = Domain.Entities.DisbursementEvidence.Attach(
            d, EvidenceKind.Invoice, amount, "CRC", "F-INV", new DateOnly(2026, 7, 16),
            "inv.pdf", "disbursement-evidence/application/1/1/x.pdf", 1024, "application/pdf", Actor);
        ctx.DisbursementEvidence.Add(evidence);
        ctx.DisbursementLineAllocations.Add(Domain.Entities.DisbursementLineAllocation.For(d.Id, itemId, amount));
        await ctx.SaveChangesAsync();

        // Flip to Validated (the completeness projection filters on d.State == Validated).
        typeof(Domain.Entities.Disbursement).GetProperty(nameof(Domain.Entities.Disbursement.State))!
            .SetValue(d, DisbursementState.Validated);
        await ctx.SaveChangesAsync();
    }

    /// <summary>Seeds a RECORDED (not validated) disbursement paying <paramref name="itemId"/>, so the
    /// closure gate's "all attributed payments validated" leg fires.</summary>
    public static async Task SeedRecordedPaymentAsync(AppDbContext ctx, int appId, int itemId, decimal amount)
    {
        var app = await ctx.Applications.FirstAsync(a => a.Id == appId);
        var d = Domain.Entities.Disbursement.Record(app, Actor, new DateOnly(2026, 7, 16), amount, "TX-R", null);
        ctx.Disbursements.Add(d);
        await ctx.SaveChangesAsync();
        ctx.DisbursementLineAllocations.Add(Domain.Entities.DisbursementLineAllocation.For(d.Id, itemId, amount));
        await ctx.SaveChangesAsync();
    }

    public static BudgetLineClosureService NewClosureService(AppDbContext ctx) =>
        new(ctx, NewCompletenessProjection(ctx), new AdminAuditEventWriter(ctx),
            new Reconciliation.NoOpReconciliationMaterializer());

    public static Stream Pdf() => new MemoryStream("%PDF-1.4 body"u8.ToArray());
}
