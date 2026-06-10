using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 021 / US9 / FR-035–FR-037, FR-040 — truth table for the applicant-initiated
/// removal decision <see cref="AppEntity.RemoveByApplicant"/>. The domain owns the
/// state guard and the "notify reviewers?" decision (Constitution II).
/// </summary>
[TestFixture]
public class ApplicationApplicantRemovalTests
{
    private static AppEntity NewApp() => new(applicantId: 1, groupId: 1, companyName: "Sazón Vegetariano");

    private static void ForceState(AppEntity app, ApplicationState state)
        => typeof(AppEntity).GetProperty(nameof(AppEntity.State))!.SetValue(app, state);

    [Test]
    public void Draft_IsDeleted_NoReviewerNotification()
    {
        var app = NewApp(); // starts Draft

        var outcome = app.RemoveByApplicant();

        Assert.That(outcome.Kind, Is.EqualTo(ApplicantRemovalKind.DraftDeleted));
        Assert.That(outcome.NotifyReviewers, Is.False);
        Assert.That(outcome.PriorState, Is.EqualTo(ApplicationState.Draft));
        Assert.That(app.IsDeleted, Is.True);
    }

    [Test]
    public void Submitted_IsWithdrawn_NoReviewerNotification()
    {
        var app = NewApp();
        ForceState(app, ApplicationState.Submitted);

        var outcome = app.RemoveByApplicant();

        Assert.That(outcome.Kind, Is.EqualTo(ApplicantRemovalKind.Withdrawn));
        Assert.That(outcome.NotifyReviewers, Is.False);
        Assert.That(outcome.PriorState, Is.EqualTo(ApplicationState.Submitted));
        Assert.That(app.IsDeleted, Is.True);
    }

    [Test]
    public void UnderReview_IsWithdrawn_NotifiesReviewers()
    {
        var app = NewApp();
        ForceState(app, ApplicationState.UnderReview);

        var outcome = app.RemoveByApplicant();

        Assert.That(outcome.Kind, Is.EqualTo(ApplicantRemovalKind.Withdrawn));
        Assert.That(outcome.NotifyReviewers, Is.True);
        Assert.That(outcome.PriorState, Is.EqualTo(ApplicationState.UnderReview));
        Assert.That(app.IsDeleted, Is.True);
    }

    [TestCase(ApplicationState.Resolved)]
    [TestCase(ApplicationState.AppealOpen)]
    [TestCase(ApplicationState.ResponseFinalized)]
    [TestCase(ApplicationState.AgreementExecuted)]
    public void TerminalStates_Throw_AndDoNotDelete(ApplicationState state)
    {
        var app = NewApp();
        ForceState(app, state);

        Assert.Throws<InvalidOperationException>(() => app.RemoveByApplicant());
        Assert.That(app.IsDeleted, Is.False);
    }

    [Test]
    public void AlreadyDeletedDraft_IsIdempotentNoOp()
    {
        var app = NewApp();
        app.RemoveByApplicant(); // first delete

        var second = app.RemoveByApplicant();

        Assert.That(second.Kind, Is.EqualTo(ApplicantRemovalKind.NoOp));
        Assert.That(second.NotifyReviewers, Is.False);
    }

    [Test]
    public void AlreadyWithdrawnUnderReview_SecondCallDoesNotReNotify()
    {
        var app = NewApp();
        ForceState(app, ApplicationState.UnderReview);
        var first = app.RemoveByApplicant();
        Assert.That(first.NotifyReviewers, Is.True);

        var second = app.RemoveByApplicant();

        Assert.That(second.Kind, Is.EqualTo(ApplicantRemovalKind.NoOp));
        Assert.That(second.NotifyReviewers, Is.False, "soft-delete is idempotent; a repeat must not re-enqueue reviewer mail");
    }
}
