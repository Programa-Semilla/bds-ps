using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Application;

[TestFixture]
public class ReviewServiceGenerateAgreementQueueTests
{
    [Test]
    public async Task GetGenerateAgreementQueueAsync_MapsApplicationsToRowDtos_UsingLatestResponseSubmittedAt()
    {
        var repo = Substitute.For<IApplicationRepository>();
        var logger = Substitute.For<ILogger<ReviewService>>();

        var (olderApp, olderTimestamp) = BuildResponseFinalizedApplication(
            applicationId: 101, firstName: "Alice", lastName: "Older", responseAtUtc: new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc));
        var (newerApp, newerTimestamp) = BuildResponseFinalizedApplication(
            applicationId: 102, firstName: "Bob", lastName: "Newer", responseAtUtc: new DateTime(2026, 4, 22, 15, 30, 0, DateTimeKind.Utc));

        repo.GetPendingAgreementPagedAsync(page: 1, pageSize: 25)
            .Returns((new List<AppEntity> { olderApp, newerApp }, TotalCount: 2));

        var service = new ReviewService(repo, logger);

        var (items, totalCount) = await service.GetGenerateAgreementQueueAsync(page: 1);

        Assert.That(totalCount, Is.EqualTo(2));
        Assert.That(items, Has.Count.EqualTo(2));

        Assert.That(items[0].ApplicationId, Is.EqualTo(101));
        Assert.That(items[0].ApplicantDisplayName, Is.EqualTo("Alice Older"));
        Assert.That(items[0].ResponseFinalizedAtUtc, Is.EqualTo(olderTimestamp));

        Assert.That(items[1].ApplicationId, Is.EqualTo(102));
        Assert.That(items[1].ApplicantDisplayName, Is.EqualTo("Bob Newer"));
        Assert.That(items[1].ResponseFinalizedAtUtc, Is.EqualTo(newerTimestamp));
    }

    [Test]
    public async Task GetGenerateAgreementQueueAsync_ClampsPageToOne_WhenGivenZero()
    {
        var repo = Substitute.For<IApplicationRepository>();
        var logger = Substitute.For<ILogger<ReviewService>>();

        repo.GetPendingAgreementPagedAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns((new List<AppEntity>(), 0));

        var service = new ReviewService(repo, logger);

        await service.GetGenerateAgreementQueueAsync(page: 0);

        await repo.Received(1).GetPendingAgreementPagedAsync(page: 1, pageSize: 25);
    }

    private static (AppEntity App, DateTime LatestResponseAt) BuildResponseFinalizedApplication(
        int applicationId,
        string firstName,
        string lastName,
        DateTime responseAtUtc)
    {
        // Applicant constructor: (userId, legalId, firstName, lastName, email, phone, performanceScore)
        var applicant = new Applicant(
            userId: $"user-{applicationId}",
            legalId: $"LID-{applicationId}",
            firstName: firstName,
            lastName: lastName,
            email: $"{firstName.ToLower()}@test.com",
            phone: null,
            performanceScore: null);
        typeof(Applicant).GetProperty("Id")!.SetValue(applicant, applicationId);

        var application = new AppEntity(applicantId: applicationId, companyName: "Test Company");
        typeof(AppEntity).GetProperty("Id")!.SetValue(application, applicationId);
        typeof(AppEntity).GetProperty("Applicant")!.SetValue(application, applicant);
        typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.ResponseFinalized);

        var response = (ApplicantResponse)Activator.CreateInstance(
            typeof(ApplicantResponse),
            nonPublic: true)!;
        typeof(ApplicantResponse).GetProperty("SubmittedAt")!.SetValue(response, responseAtUtc);
        typeof(ApplicantResponse).GetProperty("ApplicationId")!.SetValue(response, applicationId);

        var responsesField = typeof(AppEntity)
            .GetField("_applicantResponses", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var responses = (List<ApplicantResponse>)responsesField.GetValue(application)!;
        responses.Add(response);

        return (application, responseAtUtc);
    }
}
