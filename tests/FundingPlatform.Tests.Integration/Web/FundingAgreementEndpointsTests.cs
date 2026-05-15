using FundingPlatform.Application.FundingAgreements.Queries;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Web;

[TestFixture]
public class FundingAgreementEndpointsTests
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
    public async Task Generate_HappyPath_CreatesAgreementRow()
    {
        var dbName = $"ep-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

            var application = await service.LoadForGenerationAsync(applicationId);
            Assert.That(application, Is.Not.Null);

            var result = await service.PersistGenerationAsync(
                application!, "admin-user", "agreement.pdf", size: 1024, blobKey: "/store/agreement.pdf");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Agreement, Is.Not.Null);
            Assert.That(result.Agreement!.Size, Is.EqualTo(1024));
        }

        using (var ctx = CreateContext(dbName))
        {
            var count = await ctx.FundingAgreements.CountAsync(f => f.ApplicationId == applicationId);
            Assert.That(count, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Generate_WhenPreconditionsFail_ReturnsErrors_AndNoRow()
    {
        var dbName = $"ep-block-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedApplicationInDraft(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

            var application = await service.LoadForGenerationAsync(applicationId);
            Assert.That(application, Is.Not.Null);

            var result = await service.PersistGenerationAsync(
                application!, "admin-user", "agreement.pdf", 1024, "/store/x.pdf");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        }

        using (var ctx = CreateContext(dbName))
        {
            var count = await ctx.FundingAgreements.CountAsync(f => f.ApplicationId == applicationId);
            Assert.That(count, Is.EqualTo(0), "No agreement row must be created when preconditions fail.");
        }
    }

    [Test]
    public async Task GetPanel_ForUnauthorizedUser_ReturnsUnauthorized()
    {
        var dbName = $"ep-unauth-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

            var result = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
                ApplicationId: applicationId,
                UserId: "stranger-user-id",
                IsAdministrator: false,
                IsReviewerAssigned: false));

            Assert.That(result.Authorized, Is.False);
            Assert.That(result.Panel, Is.Null);
        }
    }

    [Test]
    public async Task GetPanel_ForOwningApplicant_SeesDownloadButNoGenerate()
    {
        var dbName = $"ep-owner-{Guid.NewGuid():N}";

        int applicationId;
        string applicantUserId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;
            applicantUserId = application.Applicant.UserId;

            application.GenerateFundingAgreement(
                "a.pdf", "application/pdf", 1024, "/store/a.pdf", "admin-user");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

            var result = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
                ApplicationId: applicationId,
                UserId: applicantUserId,
                IsAdministrator: false,
                IsReviewerAssigned: false));

            Assert.That(result.Authorized, Is.True);
            Assert.That(result.Panel, Is.Not.Null);
            Assert.That(result.Panel!.AgreementExists, Is.True);
            Assert.That(result.Panel.CanGenerate, Is.False, "Applicant must not see Generate action.");
            Assert.That(result.Panel.CanRegenerate, Is.False, "Applicant must not see Regenerate action.");
        }
    }

    [Test]
    public async Task GetPanel_ForNonOwnerApplicant_IsUnauthorized()
    {
        var dbName = $"ep-nonowner-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;

            application.GenerateFundingAgreement(
                "a.pdf", "application/pdf", 1024, "/store/a.pdf", "admin-user");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

            var result = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
                ApplicationId: applicationId,
                UserId: "stranger-user",
                IsAdministrator: false,
                IsReviewerAssigned: false));

            Assert.That(result.Authorized, Is.False);
        }
    }

    [Test]
    public async Task Generate_POST_Rejected_When_Review_Still_In_Progress()
    {
        await AssertBlockedState(
            seed: ctx =>
            {
                var (app, _) = SeedWithState(ctx, ApplicationState.UnderReview, includeResponse: false);
                return app.Id;
            },
            expectedReasonFragment: "Review is still in progress.");
    }

    [Test]
    public async Task Generate_POST_Rejected_When_Appeal_Open()
    {
        await AssertBlockedState(
            seed: ctx =>
            {
                var (app, _) = SeedWithState(ctx, ApplicationState.AppealOpen, includeResponse: true);
                return app.Id;
            },
            expectedReasonFragment: "appeal is currently open");
    }

    [Test]
    public async Task Generate_POST_Rejected_When_AllRejected()
    {
        await AssertBlockedState(
            seed: ctx =>
            {
                var (app, _) = SeedWithState(
                    ctx, ApplicationState.ResponseFinalized,
                    includeResponse: true, rejectAllItems: true);
                return app.Id;
            },
            expectedReasonFragment: "Nothing to fund");
    }

    private async Task AssertBlockedState(
        Func<AppDbContext, int> seed,
        string expectedReasonFragment)
    {
        var dbName = $"block-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            applicationId = seed(ctx);
            await ctx.SaveChangesAsync();
        }

        using var ctx2 = CreateContext(dbName);
        var repo = new ApplicationRepository(ctx2);
        var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

        var application = (await service.LoadForGenerationAsync(applicationId))!;
        var result = await service.PersistGenerationAsync(
            application, "admin-user", "x.pdf", 1024, "/store/x.pdf");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
        // Spec 012 — domain-rule rejection now flows as a UserFacingError with
        // the original English domain message preserved as `Detail` for log /
        // assertion purposes. The Web layer translates the Code, not Detail.
        var firstError = result.Errors.First();
        Assert.That(
            firstError.Code,
            Is.EqualTo(FundingPlatform.Application.Errors.UserFacingErrorCode.OperationRejected));
        Assert.That(
            firstError.Detail,
            Does.Contain(expectedReasonFragment).IgnoreCase);

        var count = await ctx2.FundingAgreements.CountAsync(f => f.ApplicationId == applicationId);
        Assert.That(count, Is.EqualTo(0));
    }

    private static (AppEntity app, int itemId) SeedWithState(
        AppDbContext ctx,
        ApplicationState state,
        bool includeResponse,
        bool rejectAllItems = false)
    {
        var applicant = new Applicant(
            userId: $"user-{Guid.NewGuid():N}",
            legalId: "LEG-1",
            firstName: "Test",
            lastName: "App",
            email: "t@x",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        ctx.SaveChanges();

        var category = new Category("Cat", "desc", isActive: true);
        ctx.Categories.Add(category);
        ctx.SaveChanges();

        var application = new AppEntity(applicant.Id, "Test Company");
        application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        application.AddItem(new Item("Item 1", category.Id, "specs"));
        ctx.Applications.Add(application);
        ctx.SaveChanges();

        var itemId = application.Items[0].Id;

        if (includeResponse)
        {
            typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Resolved);
            var decision = rejectAllItems ? ItemResponseDecision.Reject : ItemResponseDecision.Accept;
            application.SubmitResponse(
                new Dictionary<int, ItemResponseDecision> { [itemId] = decision },
                submittedByUserId: applicant.UserId);
        }

        typeof(AppEntity).GetProperty("State")!.SetValue(application, state);
        ctx.SaveChanges();

        return (application, itemId);
    }

    [Test]
    public async Task NonDisclosing_Response_Is_Identical_Whether_Agreement_Exists()
    {
        var dbName = $"nondisc-{Guid.NewGuid():N}";

        int appWithoutAgreement;
        int appWithAgreement;
        using (var ctx = CreateContext(dbName))
        {
            var a = SeedAcceptedApplication(ctx);
            var b = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            appWithoutAgreement = a.Id;
            appWithAgreement = b.Id;

            b.GenerateFundingAgreement("x.pdf", "application/pdf", 1, "/x", "admin-user");
            await ctx.SaveChangesAsync();
        }

        using var ctx2 = CreateContext(dbName);
        var repo = new ApplicationRepository(ctx2);
        var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

        var noAgreementResult = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
            ApplicationId: appWithoutAgreement,
            UserId: "stranger",
            IsAdministrator: false,
            IsReviewerAssigned: false));

        var withAgreementResult = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
            ApplicationId: appWithAgreement,
            UserId: "stranger",
            IsAdministrator: false,
            IsReviewerAssigned: false));

        Assert.That(noAgreementResult.Authorized, Is.EqualTo(withAgreementResult.Authorized),
            "Authorized flag must be identical whether or not the agreement exists.");
        Assert.That(noAgreementResult.Panel, Is.EqualTo(withAgreementResult.Panel),
            "Panel must be null in both cases; presence of the agreement must not leak.");
        Assert.That(noAgreementResult.Authorized, Is.False);
        Assert.That(noAgreementResult.Panel, Is.Null);
    }

    [Test]
    public async Task NonDisclosing_Response_Is_Identical_For_Missing_Application()
    {
        var dbName = $"nondisc-missing-{Guid.NewGuid():N}";
        using var ctx = CreateContext(dbName);

        var repo = new ApplicationRepository(ctx);
        var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

        var missingAppResult = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
            ApplicationId: 999999,
            UserId: "any-user",
            IsAdministrator: false,
            IsReviewerAssigned: false));

        Assert.That(missingAppResult.Authorized, Is.False);
        Assert.That(missingAppResult.Panel, Is.Null);
    }

    [Test]
    public async Task Reviewer_Assigned_Can_AccessPanel_And_Generate()
    {
        var dbName = $"rev-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;
        }

        using var ctx2 = CreateContext(dbName);
        var repo = new ApplicationRepository(ctx2);
        var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

        var panelResult = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
            ApplicationId: applicationId,
            UserId: "reviewer-user-id",
            IsAdministrator: false,
            IsReviewerAssigned: true));

        Assert.That(panelResult.Authorized, Is.True);
        Assert.That(panelResult.Panel!.CanGenerate, Is.True);

        var application2 = (await service.LoadForGenerationAsync(applicationId))!;
        var generateResult = await service.PersistGenerationAsync(
            application2, "reviewer-user-id", "r.pdf", 100, "/store/r.pdf");

        Assert.That(generateResult.Success, Is.True);
        Assert.That(generateResult.Agreement!.GeneratedByUserId, Is.EqualTo("reviewer-user-id"));
    }

    [Test]
    public async Task Regeneration_Overwrites_Prior_Metadata_With_One_Row()
    {
        var dbName = $"ep-regen-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;
        }

        // First generation
        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);
            var application = (await service.LoadForGenerationAsync(applicationId))!;
            var result = await service.PersistGenerationAsync(
                application, "admin-1", "a.pdf", 100, "/store/a.pdf");
            Assert.That(result.Success, Is.True);
        }

        DateTime originalGeneratedAt;
        using (var ctx = CreateContext(dbName))
        {
            var agreement = await ctx.FundingAgreements.FirstAsync(f => f.ApplicationId == applicationId);
            originalGeneratedAt = agreement.GeneratedAtUtc;
        }

        // Regeneration
        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);
            var application = (await service.LoadForGenerationAsync(applicationId))!;
            var result = await service.PersistGenerationAsync(
                application, "reviewer-1", "b.pdf", 200, "/store/b.pdf");
            Assert.That(result.Success, Is.True);
        }

        using (var ctx = CreateContext(dbName))
        {
            var agreements = await ctx.FundingAgreements
                .Where(f => f.ApplicationId == applicationId)
                .ToListAsync();

            Assert.That(agreements, Has.Count.EqualTo(1), "Regeneration must mutate, not duplicate.");
            Assert.That(agreements[0].FileName, Is.EqualTo("b.pdf"));
            Assert.That(agreements[0].Size, Is.EqualTo(200));
            Assert.That(agreements[0].BlobKey, Is.EqualTo("/store/b.pdf"));
            Assert.That(agreements[0].GeneratedByUserId, Is.EqualTo("reviewer-1"));
            Assert.That(agreements[0].GeneratedAtUtc, Is.GreaterThanOrEqualTo(originalGeneratedAt));
        }
    }

    [Test]
    public async Task GetPanel_ForAdministrator_ReturnsPanel_WithCanGenerate()
    {
        var dbName = $"ep-admin-{Guid.NewGuid():N}";

        int applicationId;
        using (var ctx = CreateContext(dbName))
        {
            var application = SeedAcceptedApplication(ctx);
            await ctx.SaveChangesAsync();
            applicationId = application.Id;
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var service = new FundingAgreementService(repo, NullLogger<FundingAgreementService>.Instance);

            var result = await service.GetPanelAsync(new GetFundingAgreementPanelQuery(
                ApplicationId: applicationId,
                UserId: null,
                IsAdministrator: true,
                IsReviewerAssigned: false));

            Assert.That(result.Authorized, Is.True);
            Assert.That(result.Panel, Is.Not.Null);
            Assert.That(result.Panel!.CanGenerate, Is.True);
            Assert.That(result.Panel.AgreementExists, Is.False);
        }
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

        var application = new AppEntity(applicant.Id, "Test Company");
        application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        application.AddItem(new Item("Laptop", category.Id, "specs"));
        typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Resolved);
        ctx.Applications.Add(application);
        ctx.SaveChanges();

        var itemId = application.Items[0].Id;
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [itemId] = ItemResponseDecision.Accept },
            submittedByUserId: applicant.UserId);

        return application;
    }

    private static AppEntity SeedApplicationInDraft(AppDbContext ctx)
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

        var application = new AppEntity(applicant.Id, "Test Company");
        application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
        application.AddItem(new Item("Laptop", category.Id, "specs"));
        ctx.Applications.Add(application);
        ctx.SaveChanges();

        return application;
    }
}
