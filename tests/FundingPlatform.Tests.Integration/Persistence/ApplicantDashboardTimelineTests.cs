using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Persistence;

[TestFixture]
public class ApplicantDashboardTimelineTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Test]
    public async Task SubmittedApplication_DashboardCard_ReportsSubmittedAsCurrent()
    {
        // RED: today the projection feeds an Application with an empty VersionHistory
        // (no Include on the dashboard query path), so JourneyStageResolver always
        // returns Draft and the mini timeline pins the first dot.
        var dbName = $"dash-{Guid.NewGuid():N}";

        int applicantId;

        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "LEG-1",
                firstName: "Ada",
                lastName: "Lovelace",
                email: "ada@example.com",
                phone: null,
                performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();
            applicantId = applicant.Id;

            var category = new Category("Cat", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id, "Test Company");
            application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
            application.AddItem(new Item("Widget", category.Id, "specs"));
            // Move state to Submitted directly + record matching VersionHistory entry.
            typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Submitted);
            application.AddVersionHistory(new VersionHistory(applicant.UserId, "Submitted", null));

            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var stageMappings = new StageMappingProvider();
            var resolver = new JourneyStageResolver(stageMappings);
            var journey = new JourneyProjector(resolver, stageMappings);
            var copy = Substitute.For<IApplicantCopyProvider>();
            var projection = new ApplicantDashboardProjection(repo, journey, copy);

            var dto = await projection.GetForUserAsync(applicantId, "Ada", CancellationToken.None);

            Assert.That(dto.ActiveApplications, Has.Count.EqualTo(1), "applicant has one active application");
            var card = dto.ActiveApplications[0];
            var current = card.JourneyMini.Mainline.FirstOrDefault(n => n.State == JourneyNodeState.Current);

            Assert.That(current, Is.Not.Null, "mini journey must have a current node");
            Assert.That(current!.Stage, Is.EqualTo(JourneyStage.Submitted),
                "current stage should reflect VersionHistory; bug pins it to Draft");
            Assert.That(card.CurrentStageLabel, Is.EqualTo("Submitted"));
        }
    }

    [Test]
    public async Task SubmittedApplication_DashboardRecentActivity_IsPopulated()
    {
        var dbName = $"dash-{Guid.NewGuid():N}";
        int applicantId;

        using (var ctx = CreateContext(dbName))
        {
            var applicant = new Applicant(
                userId: $"user-{Guid.NewGuid():N}",
                legalId: "LEG-2",
                firstName: "Grace",
                lastName: "Hopper",
                email: "grace@example.com",
                phone: null,
                performanceScore: null);
            ctx.Applicants.Add(applicant);
            await ctx.SaveChangesAsync();
            applicantId = applicant.Id;

            var category = new Category("Cat", "desc", isActive: true);
            ctx.Categories.Add(category);
            await ctx.SaveChangesAsync();

            var application = new AppEntity(applicant.Id, "Test Company");
            application.AssignPublicCode(FundingPlatform.Tests.Integration.Helpers.TestPublicCodes.Next());
            application.AddItem(new Item("Widget", category.Id, "specs"));
            typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Submitted);
            application.AddVersionHistory(new VersionHistory(applicant.UserId, "Created", null));
            application.AddVersionHistory(new VersionHistory(applicant.UserId, "Submitted", null));

            ctx.Applications.Add(application);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var repo = new ApplicationRepository(ctx);
            var stageMappings = new StageMappingProvider();
            var resolver = new JourneyStageResolver(stageMappings);
            var journey = new JourneyProjector(resolver, stageMappings);
            var copy = Substitute.For<IApplicantCopyProvider>();
            var projection = new ApplicantDashboardProjection(repo, journey, copy);

            var dto = await projection.GetForUserAsync(applicantId, "Grace", CancellationToken.None);

            Assert.That(dto.RecentActivity, Has.Count.EqualTo(2),
                "Recent activity must surface VersionHistory entries; bug leaves it empty.");
        }
    }
}
