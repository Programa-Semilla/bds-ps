using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 040 / T020 — gated transitions for the auditor workflow stage, the auditor
/// generation gate, and the PDF-confirmation lifecycle on <see cref="FundingAgreement"/>.
/// </summary>
[TestFixture]
public class AuditorWorkflowTransitionTests
{
    private const string ReviewerId = "reviewer-1";
    private const string AuditorId = "auditor-1";

    // --- SendToAudit ---

    [Test]
    public void SendToAudit_FromResponseFinalized_ChecklistComplete_TransitionsToPendingAudit()
    {
        var app = BuildFinalizedApplication(accept: new[] { 1 });

        var vh = app.SendToAudit(ReviewerId, reviewerChecklistComplete: true);

        Assert.That(app.State, Is.EqualTo(ApplicationState.PendingAudit));
        Assert.That(vh.Action, Is.EqualTo("SentToAudit"));
        Assert.That(app.VersionHistory, Does.Contain(vh));
    }

    [Test]
    public void SendToAudit_WhenChecklistIncomplete_Throws()
    {
        var app = BuildFinalizedApplication(accept: new[] { 1 });

        Assert.Throws<InvalidOperationException>(
            () => app.SendToAudit(ReviewerId, reviewerChecklistComplete: false));
        Assert.That(app.State, Is.EqualTo(ApplicationState.ResponseFinalized));
    }

    [Test]
    public void SendToAudit_WhenNotResponseFinalized_Throws()
    {
        var app = new AppEntity(1, 1, null, "Test Company");
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.UnderReview);

        Assert.Throws<InvalidOperationException>(
            () => app.SendToAudit(ReviewerId, reviewerChecklistComplete: true));
    }

    [Test]
    public void SendToAudit_WhenAgreementAlreadyExists_Throws()
    {
        var app = BuildPendingAuditApplication();
        app.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AuditorId);
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.ResponseFinalized);

        Assert.Throws<InvalidOperationException>(
            () => app.SendToAudit(ReviewerId, reviewerChecklistComplete: true));
    }

    // --- ReturnFromAudit / ResendToAudit (the loop) ---

    [Test]
    public void ReturnFromAudit_FromPendingAudit_TransitionsToReturnedFromAudit()
    {
        var app = BuildPendingAuditApplication();

        var vh = app.ReturnFromAudit(AuditorId);

        Assert.That(app.State, Is.EqualTo(ApplicationState.ReturnedFromAudit));
        Assert.That(vh.Action, Is.EqualTo("ReturnedFromAudit"));
    }

    [Test]
    public void ReturnFromAudit_WhenNotPendingAudit_Throws()
    {
        var app = BuildFinalizedApplication(accept: new[] { 1 });

        Assert.Throws<InvalidOperationException>(() => app.ReturnFromAudit(AuditorId));
    }

    [Test]
    public void ResendToAudit_FromReturnedFromAudit_ChecklistComplete_TransitionsToPendingAudit()
    {
        var app = BuildPendingAuditApplication();
        app.ReturnFromAudit(AuditorId);

        var vh = app.ResendToAudit(ReviewerId, reviewerChecklistComplete: true);

        Assert.That(app.State, Is.EqualTo(ApplicationState.PendingAudit));
        Assert.That(vh.Action, Is.EqualTo("ResentToAudit"));
    }

    [Test]
    public void ResendToAudit_WhenChecklistIncomplete_Throws()
    {
        var app = BuildPendingAuditApplication();
        app.ReturnFromAudit(AuditorId);

        Assert.Throws<InvalidOperationException>(
            () => app.ResendToAudit(ReviewerId, reviewerChecklistComplete: false));
    }

    // --- ReleaseForSignature ---

    [Test]
    public void ReleaseForSignature_WithoutAgreement_Throws()
    {
        var app = BuildPendingAuditApplication();

        Assert.Throws<InvalidOperationException>(() => app.ReleaseForSignature(AuditorId));
    }

    [Test]
    public void ReleaseForSignature_WithoutConfirmation_Throws()
    {
        var app = BuildPendingAuditApplication();
        app.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AuditorId);

        Assert.Throws<InvalidOperationException>(() => app.ReleaseForSignature(AuditorId));
    }

    [Test]
    public void ReleaseForSignature_AfterConfirm_TransitionsToResponseFinalized()
    {
        var app = BuildPendingAuditApplication();
        app.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AuditorId);
        app.ConfirmAgreementPdf(AuditorId);

        var vh = app.ReleaseForSignature(AuditorId);

        Assert.That(app.State, Is.EqualTo(ApplicationState.ResponseFinalized));
        Assert.That(vh.Action, Is.EqualTo("ReleasedForSignature"));
        Assert.That(app.FundingAgreement, Is.Not.Null);
    }

    // --- CanAuditorGenerateFundingAgreement ---

    [Test]
    public void CanAuditorGenerate_PendingAudit_ChecklistComplete_AcceptedItem_True()
    {
        var app = BuildPendingAuditApplication();

        Assert.That(app.CanAuditorGenerateFundingAgreement(true, out var errors), Is.True);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void CanAuditorGenerate_WhenChecklistIncomplete_False()
    {
        var app = BuildPendingAuditApplication();

        Assert.That(app.CanAuditorGenerateFundingAgreement(false, out var errors), Is.False);
        Assert.That(errors, Is.Not.Empty);
    }

    [Test]
    public void CanAuditorGenerate_WhenNotPendingAudit_False()
    {
        var app = BuildFinalizedApplication(accept: new[] { 1 });

        Assert.That(app.CanAuditorGenerateFundingAgreement(true, out _), Is.False);
    }

    // --- FundingAgreement.ConfirmByAuditor + clear on Replace ---

    [Test]
    public void ConfirmAgreementPdf_SetsConfirmation_RegenerateClearsIt()
    {
        var app = BuildPendingAuditApplication();
        var agreement = app.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AuditorId);

        app.ConfirmAgreementPdf(AuditorId);
        Assert.That(agreement.AuditorConfirmedAtUtc, Is.Not.Null);
        Assert.That(agreement.AuditorConfirmedByUserId, Is.EqualTo(AuditorId));

        app.RegenerateFundingAgreement("b.pdf", "application/pdf", 1, "/b", AuditorId);
        Assert.That(agreement.AuditorConfirmedAtUtc, Is.Null);
        Assert.That(agreement.AuditorConfirmedByUserId, Is.Null);
    }

    [Test]
    public void ConfirmAgreementPdf_WithoutAgreement_Throws()
    {
        var app = BuildPendingAuditApplication();

        Assert.Throws<InvalidOperationException>(() => app.ConfirmAgreementPdf(AuditorId));
    }

    // --- helpers ---

    private static AppEntity BuildPendingAuditApplication()
    {
        var app = BuildFinalizedApplication(accept: new[] { 1 });
        app.SendToAudit(ReviewerId, reviewerChecklistComplete: true);
        return app;
    }

    private static AppEntity BuildFinalizedApplication(int[] accept)
    {
        var app = new AppEntity(applicantId: 1, 1, null, companyName: "Test Company");
        foreach (var id in accept)
        {
            var item = new Item("p", 1);
            typeof(Item).GetProperty("Id")!.SetValue(item, id);
            app.AddItem(item);
        }

        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.Resolved);
        var decisions = accept.ToDictionary(id => id, _ => ItemResponseDecision.Accept);
        app.SubmitResponse(decisions, "applicant-user");
        return app;
    }
}
