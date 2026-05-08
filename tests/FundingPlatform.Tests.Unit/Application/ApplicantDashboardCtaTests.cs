using System.Reflection;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Interfaces;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// Spec 012 voice guide + Spec 011 awaiting-action surface. The applicant home
/// dashboard's call-to-action labels must be rendered in es-CR Spanish (formal
/// usted) via <see cref="IApplicantCopyProvider"/>, and the CTA href must point
/// at the canonical <see cref="FundingPlatform.Web.Controllers.FundingAgreementController.Details"/>
/// route (<c>/Applications/{id}/FundingAgreement</c>) — not the historical
/// hand-built <c>/FundingAgreement/Details/{id}</c> path that returns 404 because
/// the controller is attribute-routed under <c>Applications/{id}/FundingAgreement</c>.
/// </summary>
[TestFixture]
public class ApplicantDashboardCtaTests
{
    [Test]
    public async Task AwaitingAction_AgreementGenerated_ReturnsLocalizedCtaAndCanonicalUrl()
    {
        var application = BuildApplicationAwaitingSignature();
        var projection = BuildProjection(application);

        var dto = await projection.GetForUserAsync(application.ApplicantId, "Ada", CancellationToken.None);

        Assert.That(dto.AwaitingAction, Is.Not.Null,
            "Application with a generated FundingAgreement must surface an AwaitingAction.");
        Assert.That(dto.AwaitingAction!.PrimaryCtaLabel, Is.EqualTo("Firmar convenio"),
            "Awaiting-action CTA label must be the localized es-CR string per spec 012 voice guide.");
        Assert.That(dto.AwaitingAction.PrimaryCtaHref,
            Is.EqualTo($"/Applications/{application.Id}/FundingAgreement"),
            "Awaiting-action CTA href must match FundingAgreementController.Details (route '/Applications/{id}/FundingAgreement').");
    }

    [Test]
    public async Task ApplicationCard_AgreementGenerated_ReturnsLocalizedCtaAndCanonicalUrl()
    {
        var application = BuildApplicationAwaitingSignature();
        var projection = BuildProjection(application);

        var dto = await projection.GetForUserAsync(application.ApplicantId, "Ada", CancellationToken.None);

        Assert.That(dto.ActiveApplications, Has.Count.EqualTo(1));
        var primary = dto.ActiveApplications[0].PrimaryAction;
        Assert.That(primary.Label, Is.EqualTo("Firmar convenio"),
            "Application-card CTA label must be the localized es-CR string per spec 012 voice guide.");
        Assert.That(primary.Href, Is.EqualTo($"/Applications/{application.Id}/FundingAgreement"),
            "Application-card CTA href must match FundingAgreementController.Details.");
    }

    [Test]
    public async Task ApplicationCard_DraftState_ReturnsLocalizedContinueLabel()
    {
        var application = BuildDraftApplication();
        var projection = BuildProjection(application);

        var dto = await projection.GetForUserAsync(application.ApplicantId, "Ada", CancellationToken.None);

        Assert.That(dto.ActiveApplications, Has.Count.EqualTo(1));
        var primary = dto.ActiveApplications[0].PrimaryAction;
        Assert.That(primary.Label, Is.EqualTo("Continuar con la solicitud"));
        Assert.That(primary.Href, Is.EqualTo($"/Application/Details/{application.Id}"));
    }

    private static ApplicantDashboardProjection BuildProjection(AppEntity application)
    {
        var repo = Substitute.For<IApplicationRepository>();
        repo.GetForApplicantDashboardAsync(application.ApplicantId)
            .Returns(Task.FromResult(new List<AppEntity> { application }));

        var journey = Substitute.For<IJourneyProjector>();
        var emptyJourney = new JourneyViewModel(
            ApplicationId: Guid.Empty,
            ApplicationNumber: $"APP-{application.Id:D5}",
            CurrentMainlineStage: JourneyStage.Submitted,
            Mainline: Array.Empty<JourneyNode>(),
            Branches: Array.Empty<JourneyBranch>(),
            Variant: JourneyVariant.Mini);
        journey.Project(Arg.Any<AppEntity>(), Arg.Any<JourneyVariant>())
            .Returns(emptyJourney);
        journey.ProjectMany(Arg.Any<IReadOnlyCollection<AppEntity>>(), Arg.Any<JourneyVariant>())
            .Returns(new Dictionary<int, JourneyViewModel> { [application.Id] = emptyJourney });
        journey.DaysInCurrentState(Arg.Any<AppEntity>(), Arg.Any<DateTimeOffset>()).Returns(0);

        return new ApplicantDashboardProjection(repo, journey, new ApplicantCopyProvider());
    }

    private static AppEntity BuildApplicationAwaitingSignature()
    {
        var application = new AppEntity(applicantId: 42);
        application.AddItem(new Item("Widget", categoryId: 1, technicalSpecifications: "specs"));
        SetState(application, ApplicationState.ResponseFinalized);
        SetId(application, 7);

        // FundingAgreement has an internal constructor; use reflection to attach a
        // representative instance to the private backing field. We don't exercise
        // any of its members beyond the not-null check the projection relies on.
        var fa = (FundingAgreement)Activator.CreateInstance(typeof(FundingAgreement), nonPublic: true)!;
        typeof(AppEntity).GetField("_fundingAgreement", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(application, fa);

        return application;
    }

    private static AppEntity BuildDraftApplication()
    {
        var application = new AppEntity(applicantId: 42);
        application.AddItem(new Item("Widget", categoryId: 1, technicalSpecifications: "specs"));
        SetState(application, ApplicationState.Draft);
        SetId(application, 9);
        return application;
    }

    private static void SetState(AppEntity application, ApplicationState state)
        => typeof(AppEntity).GetProperty("State")!.SetValue(application, state);

    private static void SetId(AppEntity application, int id)
        => typeof(AppEntity).GetProperty("Id")!.SetValue(application, id);
}
