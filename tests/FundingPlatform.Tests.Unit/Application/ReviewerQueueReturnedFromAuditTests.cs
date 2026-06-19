using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Reviewer;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using NSubstitute;
using NUnit.Framework;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Application;

// Spec 040 regression — after the auditor returns an application it lands in
// ReturnedFromAudit, back in the reviewer's court to rework + re-send. The
// reviewer queue must surface it; otherwise the reviewer gets the email but
// the application never reappears on /Reviewer/Dashboard. The shipped bug:
// the projection only fetched Submitted/UnderReview/Resolved.
[TestFixture]
public class ReviewerQueueReturnedFromAuditTests
{
    [Test]
    public async Task GetForReviewerAsync_SurfacesReturnedFromAudit_InRowsAndAwaitingKpi()
    {
        var (projection, app) = BuildProjectionWithSingleReturnedFromAuditApplication(applicationId: 55);

        var dto = await projection.GetForReviewerAsync(
            reviewerId: "reviewer-1",
            firstName: "Reviewer",
            filter: ReviewerFilter.All,
            scope: ReviewerScope.Admin,
            searchTerm: null,
            ct: CancellationToken.None);

        Assert.That(dto.Rows, Has.Count.EqualTo(1));
        Assert.That(dto.Rows[0].ApplicationNumber, Is.EqualTo($"APP-{app.Id:D5}"));
        // A returned application awaits the reviewer's rework — it belongs in the KPI.
        Assert.That(dto.Kpis.AwaitingYourReview, Is.EqualTo(1));
    }

    [Test]
    public async Task GetForReviewerAsync_ReturnedFromAudit_ShowsUnderAwaitingMeFilter()
    {
        var (projection, _) = BuildProjectionWithSingleReturnedFromAuditApplication(applicationId: 55);

        var dto = await projection.GetForReviewerAsync(
            reviewerId: "reviewer-1",
            firstName: "Reviewer",
            filter: ReviewerFilter.AwaitingMe,
            scope: ReviewerScope.Admin,
            searchTerm: null,
            ct: CancellationToken.None);

        Assert.That(dto.Rows, Has.Count.EqualTo(1));
    }

    private static (ReviewerQueueProjection Projection, AppEntity App) BuildProjectionWithSingleReturnedFromAuditApplication(
        int applicationId)
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

        var app = new AppEntity(applicantId: applicationId, 1, null, companyName: "Test Company");
        typeof(AppEntity).GetProperty("Id")!.SetValue(app, applicationId);
        typeof(AppEntity).GetProperty("Applicant")!.SetValue(app, applicant);
        typeof(AppEntity).GetProperty("State")!.SetValue(app, ApplicationState.ReturnedFromAudit);

        var repo = Substitute.For<IApplicationRepository>();
        repo.GetByStateForReviewerAsync(Arg.Any<ApplicationState>(), Arg.Any<ReviewerScopeHint>(), 1, 200, Arg.Any<string?>())
            .Returns((new List<AppEntity>(), 0));
        repo.GetByStateForReviewerAsync(ApplicationState.ReturnedFromAudit, Arg.Any<ReviewerScopeHint>(), 1, 200, Arg.Any<string?>())
            .Returns((new List<AppEntity> { app }, TotalCount: 1));

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
