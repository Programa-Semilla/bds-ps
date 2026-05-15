using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.FundingAgreement;

/// <summary>
/// Spec 018 / US1 / SC-004 — branded document projection assertions. Tests
/// exercise the entity-level rules that feed the new
/// <c>FundingAgreementDocumentViewModel</c> (commission distinct-actors,
/// approved/rejected partition, supplier compliance dedupe) against an
/// EF-managed Application aggregate. The live Razor projection in
/// <c>FundingAgreementController.BuildDocumentViewModelAsync</c> consumes the
/// same shape; this test pinion the entity-side contract per Constitution II.
/// </summary>
[TestFixture]
public class BrandedDocumentProjectionTests
{
    private static AppDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Test]
    public async Task VersionHistory_DistinctReviewItemActors_AreCommissionMembers()
    {
        var dbName = $"branded-distinct-{Guid.NewGuid():N}";
        using var ctx = NewContext(dbName);

        var applicant = SeedApplicant(ctx);
        var app = new AppEntity(applicant.Id, "Sazón Vegetariano");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        // Three reviewers, two take an action; a third is assigned but does not.
        // Per FR-006, only the action-takers should appear on the cover.
        app.AddVersionHistory(new VersionHistory("user-paola", "ReviewItem", "Item 'A' — Approve"));
        app.AddVersionHistory(new VersionHistory("user-milena", "ReviewItem", "Item 'B' — Reject"));
        // Repeat action by paola — still distinct count of 2.
        app.AddVersionHistory(new VersionHistory("user-paola", "ReviewItem", "Item 'C' — Approve"));
        // Non-ReviewItem actions don't count toward the commission.
        app.AddVersionHistory(new VersionHistory("user-aldo", "Created", "Application created"));
        await ctx.SaveChangesAsync();

        var distinctActors = app.VersionHistory
            .Where(vh => vh.Action == "ReviewItem")
            .Select(vh => vh.UserId)
            .Distinct()
            .ToList();

        Assert.That(distinctActors, Is.EquivalentTo(new[] { "user-paola", "user-milena" }));
    }

    [Test]
    public async Task ApprovedAndRejected_PartitionByReviewStatus()
    {
        var dbName = $"branded-partition-{Guid.NewGuid():N}";
        using var ctx = NewContext(dbName);

        var applicant = SeedApplicant(ctx);
        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, "Sazón Vegetariano");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        var approvedItem = new Item("Laptop", category.Id, "specs");
        var rejectedItem = new Item("Antena", category.Id, "specs");
        app.AddItem(approvedItem);
        app.AddItem(rejectedItem);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        // Approve / reject directly via entity facade (mirrors what the review
        // service does in production).
        // Approve requires a quotation; for projection-shape testing we mark the
        // status field directly via reflection — the projection only reads
        // ReviewStatus, not the supplier.
        typeof(Item).GetProperty("ReviewStatus")!.SetValue(approvedItem, ItemReviewStatus.Approved);
        rejectedItem.Reject("Costo excede presupuesto");
        app.AssignLineCodeToItem(approvedItem.Id, "T1-1");
        app.AssignLineCodeToItem(rejectedItem.Id, "T1-2");
        await ctx.SaveChangesAsync();

        var approved = app.Items.Where(i => i.ReviewStatus == ItemReviewStatus.Approved).ToList();
        var rejected = app.Items.Where(i => i.ReviewStatus == ItemReviewStatus.Rejected).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(approved, Has.Count.EqualTo(1));
            Assert.That(approved[0].LineCode, Is.EqualTo("T1-1"));
            Assert.That(rejected, Has.Count.EqualTo(1));
            Assert.That(rejected[0].LineCode, Is.EqualTo("T1-2"));
            Assert.That(rejected[0].ReviewComment, Is.EqualTo("Costo excede presupuesto"));
        });
    }

    [Test]
    public async Task ZeroRejected_OmitsRejectedSection()
    {
        // Edge Case 1 — when no item is rejected, the rejected list / table /
        // header are all dropped. We assert the projection input shape: the
        // RejectedLines collection is empty, which the Razor partial uses to
        // skip the "2. Líneas no aprobadas" block.
        var dbName = $"branded-zero-rej-{Guid.NewGuid():N}";
        using var ctx = NewContext(dbName);
        var applicant = SeedApplicant(ctx);
        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, "Sazón Vegetariano");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        var item = new Item("Laptop", category.Id, "specs");
        app.AddItem(item);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        typeof(Item).GetProperty("ReviewStatus")!.SetValue(item, ItemReviewStatus.Approved);
        app.AssignLineCodeToItem(item.Id, "T1-1");
        await ctx.SaveChangesAsync();

        var rejected = app.Items.Where(i => i.ReviewStatus == ItemReviewStatus.Rejected).ToList();
        Assert.That(rejected, Is.Empty);
    }

    [Test]
    public async Task ZeroApproved_OmitsSupplierVerification()
    {
        // Edge Case 2 — when no item is approved, the supplier-verification
        // table is omitted entirely (no distinct approved suppliers).
        var dbName = $"branded-zero-app-{Guid.NewGuid():N}";
        using var ctx = NewContext(dbName);
        var applicant = SeedApplicant(ctx);
        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var app = new AppEntity(applicant.Id, "Sazón Vegetariano");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        var item = new Item("Laptop", category.Id, "specs");
        app.AddItem(item);
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        item.Reject("Out of scope");
        app.AssignLineCodeToItem(item.Id, "T1-1");
        await ctx.SaveChangesAsync();

        var approved = app.Items.Where(i => i.ReviewStatus == ItemReviewStatus.Approved).ToList();
        Assert.That(approved, Is.Empty);
    }

    [Test]
    public async Task SingleReviewer_OneCommissionMember()
    {
        var dbName = $"branded-single-{Guid.NewGuid():N}";
        using var ctx = NewContext(dbName);
        var applicant = SeedApplicant(ctx);
        var app = new AppEntity(applicant.Id, "Sazón Vegetariano");
        app.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        ctx.Applications.Add(app);
        await ctx.SaveChangesAsync();

        app.AddVersionHistory(new VersionHistory("user-paola", "ReviewItem", "Item — Approve"));
        await ctx.SaveChangesAsync();

        var commission = app.VersionHistory
            .Where(vh => vh.Action == "ReviewItem")
            .Select(vh => vh.UserId)
            .Distinct()
            .ToList();

        Assert.That(commission, Is.EqualTo(new[] { "user-paola" }));
    }

    private static Applicant SeedApplicant(AppDbContext ctx)
    {
        var applicant = new Applicant(
            userId: $"u-{Guid.NewGuid():N}",
            legalId: "1-1234-5678",
            firstName: "Daniel",
            lastName: "Centeno Bejarano",
            email: $"applicant-{Guid.NewGuid():N}@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        ctx.SaveChanges();
        return applicant;
    }
}
