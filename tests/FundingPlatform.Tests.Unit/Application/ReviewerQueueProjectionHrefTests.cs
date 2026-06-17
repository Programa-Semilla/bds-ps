using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Routing;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using NSubstitute;
using NUnit.Framework;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Application;

// Producer-side contract for the reviewer queue's "Revisar" button. The
// pre-spec-014 bug shipped "/Review/Review/{id}" hrefs that 404'd against
// ReviewController's "Review/{id:int}" route. These tests pin the producer
// to ReviewRoutes — the same constants the controller's [Route] uses — so
// the two sides cannot drift again.
[TestFixture]
public class ReviewerQueueProjectionHrefTests
{
    [Test]
    public async Task GetForReviewerAsync_RowPrimaryActionHref_MatchesControllerReviewRoute()
    {
        var (projection, app) = BuildProjectionWithSingleSubmittedApplication(applicationId: 42);

        var dto = await projection.GetForReviewerAsync(
            reviewerId: "reviewer-1",
            firstName: "Reviewer",
            filter: ReviewerFilter.All,
            scope: ReviewerScope.Admin,
            searchTerm: null,
            ct: CancellationToken.None);

        Assert.That(dto.Rows, Has.Count.EqualTo(1));
        Assert.That(dto.Rows[0].PrimaryAction.Href, Is.EqualTo(ReviewRoutes.PathFor(app.Id)));
        Assert.That(dto.Rows[0].PrimaryAction.Href, Is.EqualTo("/Review/42"));
        Assert.That(dto.Rows[0].PrimaryAction.Href, Does.Not.Contain("/Review/Review/"));
    }

    [Test]
    public async Task GetForReviewerAsync_RecentActivityDeepLink_MatchesControllerReviewRoute()
    {
        var (projection, _) = BuildProjectionWithSingleSubmittedApplication(applicationId: 42, withVersionEvent: true);

        var dto = await projection.GetForReviewerAsync(
            reviewerId: "reviewer-1",
            firstName: "Reviewer",
            filter: ReviewerFilter.All,
            scope: ReviewerScope.Admin,
            searchTerm: null,
            ct: CancellationToken.None);

        Assert.That(dto.RecentActivity, Is.Not.Empty);
        var href = dto.RecentActivity[0].DeepLinkHref;
        Assert.That(href, Does.StartWith("/Review/42#event-"));
        Assert.That(href, Does.Not.Contain("/Review/Review/"));
    }

    [Test]
    public async Task GetForReviewerAsync_RecentActivityTitle_AndRowAction_RenderInSpanish()
    {
        var (projection, _) = BuildProjectionWithSingleSubmittedApplication(applicationId: 42, withVersionEvent: true);

        var dto = await projection.GetForReviewerAsync(
            reviewerId: "reviewer-1",
            firstName: "Reviewer",
            filter: ReviewerFilter.All,
            scope: ReviewerScope.Admin,
            searchTerm: null,
            ct: CancellationToken.None);

        // es-CR: the activity title is the localized event copy, not the raw
        // internal VersionHistory action ("Submitted") that leaked before.
        Assert.That(dto.RecentActivity, Is.Not.Empty);
        Assert.That(dto.RecentActivity[0].Title, Is.EqualTo("Solicitud enviada"));
        Assert.That(dto.RecentActivity[0].Title, Does.Not.Contain("Submitted"));

        // es-CR: the row CTA label must be Spanish ("Revisar"), not "Review".
        Assert.That(dto.Rows, Has.Count.EqualTo(1));
        Assert.That(dto.Rows[0].PrimaryAction.Label, Is.EqualTo("Revisar"));
    }

    private static (ReviewerQueueProjection Projection, AppEntity App) BuildProjectionWithSingleSubmittedApplication(
        int applicationId,
        bool withVersionEvent = false)
    {
        var applicant = new Applicant(
            userId: $"user-{applicationId}",
            legalId: $"LID-{applicationId}",
            firstName: "Test",
            lastName: "Applicant",
            email: "test@example.com",
            phone: null,
            performanceScore: null);
        typeof(Applicant).GetProperty("Id")!.SetValue(applicant, applicationId);

        var app = new AppEntity(applicantId: applicationId, 1, null,companyName: "Test Company");
        typeof(AppEntity).GetProperty("Id")!.SetValue(app, applicationId);
        typeof(AppEntity).GetProperty("Applicant")!.SetValue(app, applicant);
        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.Submitted);

        if (withVersionEvent)
        {
            var entry = new VersionHistory(userId: $"user-{applicationId}", action: "Submitted", details: null);
            typeof(VersionHistory).GetProperty("Id")!.SetValue(entry, 7);
            app.AddVersionHistory(entry);
        }

        var repo = Substitute.For<IApplicationRepository>();
        // Spec 016 — projection now calls the scoped variant. Match every state
        // and pass the admin-shaped hint so the test exercises the
        // short-circuit path (this test is about routing, not group scoping).
        repo.GetByStateForReviewerAsync(ApplicationState.Submitted, Arg.Any<ReviewerScopeHint>(), 1, 200, Arg.Any<string?>())
            .Returns((new List<AppEntity> { app }, TotalCount: 1));
        repo.GetByStateForReviewerAsync(ApplicationState.UnderReview, Arg.Any<ReviewerScopeHint>(), 1, 200, Arg.Any<string?>())
            .Returns((new List<AppEntity>(), 0));
        repo.GetByStateForReviewerAsync(ApplicationState.Resolved, Arg.Any<ReviewerScopeHint>(), 1, 200, Arg.Any<string?>())
            .Returns((new List<AppEntity>(), 0));

        var config = Substitute.For<ISystemConfigurationRepository>();
        config.GetByKeyAsync(Arg.Any<string>()).Returns((SystemConfiguration?)null);

        var journey = Substitute.For<IJourneyProjector>();
        var stubJourney = new JourneyViewModel(
            ApplicationId: Guid.Empty,
            ApplicationNumber: $"APP-{applicationId:D5}",
            CurrentMainlineStage: JourneyStage.Submitted,
            Mainline: Array.Empty<JourneyNode>(),
            Branches: Array.Empty<JourneyBranch>(),
            Variant: JourneyVariant.Micro);
        journey.ProjectMany(Arg.Any<IReadOnlyCollection<AppEntity>>(), Arg.Any<JourneyVariant>())
            .Returns(new Dictionary<int, JourneyViewModel>());
        journey.Project(Arg.Any<AppEntity>(), Arg.Any<JourneyVariant>()).Returns(stubJourney);
        journey.DaysInCurrentState(Arg.Any<AppEntity>(), Arg.Any<DateTimeOffset>()).Returns(0);

        var copy = Substitute.For<IReviewerCopyProvider>();

        var projection = new ReviewerQueueProjection(repo, config, journey, copy);
        return (projection, app);
    }
}
