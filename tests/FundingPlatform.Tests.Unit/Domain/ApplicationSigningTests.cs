using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class ApplicationSigningTests
{
    private const string AdminUserId = "admin-1";
    private const string ApplicantUserId = "applicant-1";
    private const string ReviewerUserId = "reviewer-1";

    [Test]
    public void CanRegenerate_WhenNoAgreement_ReturnsFalse()
    {
        var application = BuildReadyToGenerate();

        Assert.That(application.CanRegenerateFundingAgreement(out var errors), Is.False);
        Assert.That(errors, Does.Contain("No Funding Agreement exists to regenerate."));
    }

    [Test]
    public void CanRegenerate_WithAgreementAndNoSignedUpload_ReturnsTrue()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);

        Assert.That(application.CanRegenerateFundingAgreement(out var errors), Is.True);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void CanRegenerate_WhenSignedUploadExists_ReturnsFalseWithLockdownReason()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);
        application.SubmitSignedUpload(ApplicantUserId, 1, "signed.pdf", 1024, "/store/s.pdf");

        Assert.That(application.CanRegenerateFundingAgreement(out var errors), Is.False);
        Assert.That(errors, Does.Contain("Agreement is locked: a signed upload has been submitted."));
    }

    [Test]
    public void ExecuteAgreement_FromResponseFinalized_TransitionsToAgreementExecuted()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);

        application.ExecuteAgreement(ReviewerUserId);

        Assert.That(application.State, Is.EqualTo(ApplicationState.AgreementExecuted));
    }

    [Test]
    public void ExecuteAgreement_FromOtherState_Throws()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);
        application.ExecuteAgreement(ReviewerUserId);

        Assert.Throws<InvalidOperationException>(() => application.ExecuteAgreement(ReviewerUserId));
    }

    [Test]
    public void ExecuteAgreement_WithoutFundingAgreement_Throws()
    {
        var application = BuildReadyToGenerate();

        Assert.Throws<InvalidOperationException>(() => application.ExecuteAgreement(ReviewerUserId));
    }

    [Test]
    public void SubmitSignedUpload_FromResponseFinalized_AttachesPendingUpload()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);

        var upload = application.SubmitSignedUpload(ApplicantUserId, 1, "signed.pdf", 1024, "/store/s.pdf");

        Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Pending));
        Assert.That(application.FundingAgreement!.PendingUpload, Is.SameAs(upload));
    }

    [Test]
    public void SubmitSignedUpload_WhenStateNotResponseFinalized_Throws()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);
        ApplicationResponseTransitionsTests.SetState(application, ApplicationState.Resolved);

        Assert.Throws<InvalidOperationException>(() =>
            application.SubmitSignedUpload(ApplicantUserId, 1, "s.pdf", 1, "/s"));
    }

    [Test]
    public void ApproveSignedUpload_TriggersExecuteAgreement()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);
        application.SubmitSignedUpload(ApplicantUserId, 1, "signed.pdf", 1024, "/store/s.pdf");

        var decision = application.ApproveSignedUpload(ReviewerUserId, comment: null);

        Assert.That(decision.Outcome, Is.EqualTo(SigningDecisionOutcome.Approved));
        Assert.That(application.State, Is.EqualTo(ApplicationState.AgreementExecuted));
        Assert.That(application.FundingAgreement!.PendingUpload, Is.Null);
    }

    [Test]
    public void RejectSignedUpload_DoesNotTransitionState()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);
        application.SubmitSignedUpload(ApplicantUserId, 1, "signed.pdf", 1024, "/store/s.pdf");

        var decision = application.RejectSignedUpload(ReviewerUserId, "please sign on page 3");

        Assert.That(decision.Outcome, Is.EqualTo(SigningDecisionOutcome.Rejected));
        Assert.That(application.State, Is.EqualTo(ApplicationState.ResponseFinalized));
        Assert.That(application.FundingAgreement!.PendingUpload, Is.Null, "The pending upload has transitioned to Rejected.");
    }

    [Test]
    public void RejectSignedUpload_ThenReUpload_Succeeds()
    {
        var application = BuildReadyToGenerate();
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);
        application.SubmitSignedUpload(ApplicantUserId, 1, "first.pdf", 1024, "/store/first");
        application.RejectSignedUpload(ReviewerUserId, "please retry");

        var reupload = application.SubmitSignedUpload(ApplicantUserId, 1, "second.pdf", 1024, "/store/second");

        Assert.That(reupload.Status, Is.EqualTo(SignedUploadStatus.Pending));
        Assert.That(application.FundingAgreement!.SignedUploads, Has.Count.EqualTo(2));
    }

    [Test]
    public void CanUserReviewSignedUpload_AdminOrAssignedReviewer()
    {
        var application = BuildReadyToGenerate();

        Assert.That(application.CanUserReviewSignedUpload(true, false), Is.True);
        Assert.That(application.CanUserReviewSignedUpload(false, true), Is.True);
        Assert.That(application.CanUserReviewSignedUpload(false, false), Is.False);
    }

    private static AppEntity BuildReadyToGenerate()
    {
        var application = new AppEntity(applicantId: 1, 1, companyName: "Test Company");
        AddItem(application, 1);
        ApplicationResponseTransitionsTests.SetState(application, ApplicationState.Resolved);

        var decisions = new Dictionary<int, ItemResponseDecision>
        {
            [1] = ItemResponseDecision.Accept
        };
        application.SubmitResponse(decisions, ApplicantUserId);

        return application;
    }

    private static void AddItem(AppEntity application, int itemId)
    {
        var item = new Item("p", 1);
        typeof(Item).GetProperty("Id")!.SetValue(item, itemId);
        application.AddItem(item);
    }
}
