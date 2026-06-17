using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.FundingAgreements;

[TestFixture]
public class FundingAgreementPersistenceTests
{
    private AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    [Test]
    public async Task FundingAgreement_IsPersistedAndLoadedViaApplicationNavigation()
    {
        var dbName = $"fa-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;

            application.GenerateFundingAgreement(
                fileName: "agreement.pdf",
                contentType: "application/pdf",
                size: 1024,
                blobKey: "/store/agreement.pdf",
                generatingUserId: "admin-user");

            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var loaded = await ctx.Applications
                .Include(a => a.FundingAgreement)
                .FirstAsync(a => a.Id == applicationId);

            Assert.That(loaded.FundingAgreement, Is.Not.Null);
            Assert.That(loaded.FundingAgreement!.FileName, Is.EqualTo("agreement.pdf"));
            Assert.That(loaded.FundingAgreement!.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(loaded.FundingAgreement!.Size, Is.EqualTo(1024));
            Assert.That(loaded.FundingAgreement!.BlobKey, Is.EqualTo("/store/agreement.pdf"));
            Assert.That(loaded.FundingAgreement!.GeneratedByUserId, Is.EqualTo("admin-user"));
        }
    }

    [Test]
    public async Task FundingAgreement_Regeneration_MutatesExistingRow()
    {
        var dbName = $"fa-regen-{Guid.NewGuid():N}";
        int applicationId;

        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;

            application.GenerateFundingAgreement(
                "a.pdf", "application/pdf", 1, "/a", "admin-user");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var application = await ctx.Applications
                .Include(a => a.FundingAgreement)
                .Include(a => a.ApplicantResponses)
                    .ThenInclude(r => r.ItemResponses)
                .Include(a => a.Appeals)
                .FirstAsync(a => a.Id == applicationId);

            application.RegenerateFundingAgreement(
                "b.pdf", "application/pdf", 2, "/b", "reviewer-user");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var agreements = await ctx.FundingAgreements
                .Where(f => f.ApplicationId == applicationId)
                .ToListAsync();

            Assert.That(agreements, Has.Count.EqualTo(1),
                "Regeneration must mutate the single row, not insert a duplicate.");
            Assert.That(agreements[0].FileName, Is.EqualTo("b.pdf"));
            Assert.That(agreements[0].Size, Is.EqualTo(2));
            Assert.That(agreements[0].GeneratedByUserId, Is.EqualTo("reviewer-user"));
        }
    }

    [Test]
    public async Task FundingAgreement_UniqueIndexOnApplicationId_PreventsDuplicates()
    {
        var dbName = $"fa-dup-{Guid.NewGuid():N}";

        using var ctx = CreateContext(dbName);
        var application = SeedAcceptedApplication(ctx);
        await ctx.SaveChangesAsync();

        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", "admin-user");
        await ctx.SaveChangesAsync();

        Assert.Throws<InvalidOperationException>(() =>
            application.GenerateFundingAgreement("b.pdf", "application/pdf", 1, "/b", "admin-user"));
    }

    private static AppEntity SeedAcceptedApplication(AppDbContext ctx)
    {
        var applicant = new Applicant(
            userId: $"user-{Guid.NewGuid():N}",
            legalId: "LEG-1",
            firstName: "Ana",
            lastName: "Applicant",
            email: "ana@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        ctx.SaveChanges();

        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        ctx.SaveChanges();

        var application = new AppEntity(applicant.Id, 1, null,"Test Company");
        application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        application.AddItem(new Item("Laptop", category.Id));
        typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Resolved);
        ctx.Applications.Add(application);
        ctx.SaveChanges();

        var itemId = application.Items[0].Id;
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [itemId] = ItemResponseDecision.Accept },
            submittedByUserId: applicant.UserId);

        return application;
    }
}
