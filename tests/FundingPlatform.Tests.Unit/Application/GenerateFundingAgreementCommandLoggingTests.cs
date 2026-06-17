using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Application;

[TestFixture]
public class GenerateFundingAgreementCommandLoggingTests
{
    [Test]
    public async Task PersistGeneration_OnSuccess_EmitsStructuredLogWithAllFields()
    {
        var repo = Substitute.For<IApplicationRepository>();
        var logger = Substitute.For<ILogger<FundingAgreementService>>();
        var service = new FundingAgreementService(repo, logger, Substitute.For<IUserStoreReader>(), Substitute.For<FundingPlatform.Application.Notifications.INotificationOutboxWriter>());

        var application = BuildReadyApplication();

        var result = await service.PersistGenerationAsync(
            application,
            userId: "admin-user-42",
            fileName: "agreement.pdf",
            size: 2048,
            blobKey: "/store/agreement.pdf");

        Assert.That(result.Success, Is.True);

        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state!.ToString()!.Contains("applicationId")
                && state.ToString()!.Contains("actingUserId")
                && state.ToString()!.Contains("fileSize")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task PersistGeneration_OnPreconditionFailure_EmitsStructuredLogWithFailureReason()
    {
        var repo = Substitute.For<IApplicationRepository>();
        var logger = Substitute.For<ILogger<FundingAgreementService>>();
        var service = new FundingAgreementService(repo, logger, Substitute.For<IUserStoreReader>(), Substitute.For<FundingPlatform.Application.Notifications.INotificationOutboxWriter>());

        var application = new AppEntity(applicantId: 1, 1, null,companyName: "Test Company");
        // Application is in Draft — preconditions fail.

        var result = await service.PersistGenerationAsync(
            application,
            userId: "admin-user-42",
            fileName: "agreement.pdf",
            size: 2048,
            blobKey: "/store/agreement.pdf");

        Assert.That(result.Success, Is.False);

        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state!.ToString()!.Contains("failureReason")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    private static AppEntity BuildReadyApplication()
    {
        var application = new AppEntity(applicantId: 1, 1, null,companyName: "Test Company");
        var item = new Item("Widget", 1);
        typeof(Item).GetProperty("Id")!.SetValue(item, 100);
        application.AddItem(item);
        typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Resolved);
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [100] = ItemResponseDecision.Accept },
            submittedByUserId: "applicant-user");
        return application;
    }
}
