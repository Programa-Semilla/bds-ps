using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

/// <summary>
/// In-memory EF persistence tests for SignedUpload / SigningReviewDecision.
/// Note: the SQL Server filtered unique index (<c>UX_SignedUploads_OnePending_PerAgreement</c>)
/// cannot be verified against the in-memory provider; the domain-level invariant
/// is covered by <see cref="FundingPlatform.Tests.Unit.Domain.FundingAgreementLockdownTests"/>.
/// </summary>
[TestFixture]
public class SignedUploadPersistenceTests
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
    public async Task SignedUpload_PersistsViaAggregate_AndHydratesOnLoad()
    {
        var dbName = $"su-persist-{Guid.NewGuid():N}";
        int appId;

        using (var ctx = CreateContext(dbName))
        {
            var app = SeedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;

            app.SubmitSignedUpload("applicant-user", 1, "signed.pdf", 1024, "/store/signed.pdf");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var app = await ctx.Applications
                .Include(a => a.FundingAgreement!)
                    .ThenInclude(fa => fa.SignedUploads)
                        .ThenInclude(u => u.ReviewDecision)
                .FirstAsync(a => a.Id == appId);

            Assert.That(app.FundingAgreement, Is.Not.Null);
            Assert.That(app.FundingAgreement!.SignedUploads, Has.Count.EqualTo(1));
            Assert.That(app.FundingAgreement.IsLocked, Is.True);
            var pending = app.FundingAgreement.PendingUpload;
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending!.Status, Is.EqualTo(SignedUploadStatus.Pending));
        }
    }

    [Test]
    public async Task SigningReviewDecision_IsWrittenOnApproval_AndLoadsWithUpload()
    {
        var dbName = $"su-decision-{Guid.NewGuid():N}";
        int appId;

        using (var ctx = CreateContext(dbName))
        {
            var app = SeedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;

            app.SubmitSignedUpload("applicant-user", 1, "signed.pdf", 1024, "/store/signed.pdf");
            app.ApproveSignedUpload("reviewer-1", "ok");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var decision = await ctx.SigningReviewDecisions.SingleAsync();
            Assert.That(decision.Outcome, Is.EqualTo(SigningDecisionOutcome.Approved));
            Assert.That(decision.ReviewerUserId, Is.EqualTo("reviewer-1"));

            var upload = await ctx.SignedUploads
                .Include(u => u.ReviewDecision)
                .SingleAsync();
            Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Approved));
            Assert.That(upload.ReviewDecision, Is.Not.Null);
            Assert.That(upload.ReviewDecision!.Id, Is.EqualTo(decision.Id));
        }
    }

    [Test]
    public async Task FundingAgreement_GeneratedVersion_IncrementsOnRegeneration()
    {
        var dbName = $"su-ver-{Guid.NewGuid():N}";
        int appId;

        using (var ctx = CreateContext(dbName))
        {
            var app = SeedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var app = await ctx.Applications
                .Include(a => a.FundingAgreement!)
                    .ThenInclude(fa => fa.SignedUploads)
                .Include(a => a.ApplicantResponses)
                    .ThenInclude(r => r.ItemResponses)
                .Include(a => a.Appeals)
                .FirstAsync(a => a.Id == appId);
            app.RegenerateFundingAgreement("v2.pdf", "application/pdf", 1, "/store/v2.pdf", "admin-1");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var agreement = await ctx.FundingAgreements.FirstAsync(fa => fa.ApplicationId == appId);
            Assert.That(agreement.GeneratedVersion, Is.EqualTo(2));
        }
    }

    private static AppEntity SeedApplicationWithAgreement(AppDbContext ctx)
    {
        var uniq = Guid.NewGuid().ToString("N");
        var applicant = new Applicant(
            userId: $"user-{uniq}",
            legalId: "LEG-1",
            firstName: "Ana",
            lastName: "Applicant",
            email: $"ana-{uniq}@example.com",
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

        application.GenerateFundingAgreement(
            "agreement.pdf", "application/pdf", 1024, "/store/agreement.pdf", "admin-1");
        ctx.SaveChanges();
        return application;
    }
}
