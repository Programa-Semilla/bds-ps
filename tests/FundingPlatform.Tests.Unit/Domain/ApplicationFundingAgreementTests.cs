using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class ApplicationFundingAgreementTests
{
    private const string AdminUserId = "admin-1";

    [Test]
    public void CanGenerate_WhenReviewStillInProgress_ReturnsFalseWithReviewOpenError()
    {
        var application = new AppEntity(applicantId: 1, 1, companyName: "Test Company");
        ApplicationResponseTransitionsTests.SetState(application, ApplicationState.UnderReview);

        Assert.That(application.CanGenerateFundingAgreement(out var errors), Is.False);
        Assert.That(errors, Does.Contain("Review is still in progress."));
    }

    [Test]
    public void CanGenerate_WhenAppealOpen_ReturnsFalseWithAppealError()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());
        ApplicationResponseTransitionsTests.SetState(application, ApplicationState.AppealOpen);

        Assert.That(application.CanGenerateFundingAgreement(out var errors), Is.False);
        Assert.That(errors, Does.Contain("An appeal is currently open on this application."));
    }

    [Test]
    public void CanGenerate_WhenNoResponseYet_ReturnsFalseWithPartialResponseError()
    {
        var application = new AppEntity(applicantId: 1, 1, companyName: "Test Company");
        AddItem(application, 1);
        ApplicationResponseTransitionsTests.SetState(application, ApplicationState.ResponseFinalized);

        Assert.That(application.CanGenerateFundingAgreement(out var errors), Is.False);
        Assert.That(errors, Does.Contain("Applicant has not yet responded to every approved item."));
    }

    [Test]
    public void CanGenerate_WhenAllItemsRejected_ReturnsFalseWithNothingToFund()
    {
        var application = BuildFinalizedApplication(
            acceptItemIds: Array.Empty<int>(),
            rejectItemIds: new[] { 1, 2 });

        Assert.That(application.CanGenerateFundingAgreement(out var errors), Is.False);
        Assert.That(errors, Does.Contain("Nothing to fund: all items were rejected."));
    }

    [Test]
    public void CanGenerate_HappyPath_ReturnsTrue()
    {
        var application = BuildFinalizedApplication(
            acceptItemIds: new[] { 1 },
            rejectItemIds: new[] { 2 });

        Assert.That(application.CanGenerateFundingAgreement(out var errors), Is.True);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Generate_HappyPath_AttachesAgreementToApplication()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());

        var agreement = application.GenerateFundingAgreement(
            fileName: "agreement.pdf",
            contentType: "application/pdf",
            size: 123,
            blobKey: "generated-artifacts/applicants/u-1/1/abc.pdf",
            generatingUserId: AdminUserId);

        Assert.That(application.FundingAgreement, Is.SameAs(agreement));
        Assert.That(agreement.GeneratedByUserId, Is.EqualTo(AdminUserId));
    }

    [Test]
    public void Generate_WhenAgreementAlreadyExists_Throws()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());
        application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/p", AdminUserId);

        Assert.Throws<InvalidOperationException>(() =>
            application.GenerateFundingAgreement("b.pdf", "application/pdf", 1, "/q", AdminUserId));
    }

    [Test]
    public void Regenerate_HappyPath_ReplacesMetadataAndPreservesAggregate()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());
        var original = application.GenerateFundingAgreement("a.pdf", "application/pdf", 1, "/a", AdminUserId);
        var originalTimestamp = original.GeneratedAtUtc;

        // Thread.Sleep-free timestamp bump: nothing more to do — UTC progresses on each call.
        var replaced = application.RegenerateFundingAgreement(
            "b.pdf", "application/pdf", 2, "/b", "reviewer-1");

        Assert.That(replaced, Is.SameAs(original), "Regeneration mutates in place, not replaces.");
        Assert.That(replaced.FileName, Is.EqualTo("b.pdf"));
        Assert.That(replaced.Size, Is.EqualTo(2));
        Assert.That(replaced.BlobKey, Is.EqualTo("/b"));
        Assert.That(replaced.GeneratedByUserId, Is.EqualTo("reviewer-1"));
        Assert.That(replaced.GeneratedAtUtc, Is.GreaterThanOrEqualTo(originalTimestamp));
    }

    [Test]
    public void Regenerate_WhenNoAgreementYet_Throws()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());

        Assert.Throws<InvalidOperationException>(() =>
            application.RegenerateFundingAgreement("b.pdf", "application/pdf", 1, "/b", AdminUserId));
    }

    [Test]
    public void CanUserAccess_OwnerApplicant_ReturnsTrue()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());
        SetApplicantUserId(application, "applicant-user");

        var can = application.CanUserAccessFundingAgreement(
            applicantUserId: "applicant-user",
            isAdministrator: false,
            isReviewerAssignedToThisApplication: false);

        Assert.That(can, Is.True);
    }

    [Test]
    public void CanUserAccess_NonOwnerApplicant_ReturnsFalse()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());
        SetApplicantUserId(application, "applicant-user");

        var can = application.CanUserAccessFundingAgreement(
            applicantUserId: "someone-else",
            isAdministrator: false,
            isReviewerAssignedToThisApplication: false);

        Assert.That(can, Is.False);
    }

    [Test]
    public void CanUserAccess_Administrator_ReturnsTrue()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());

        var can = application.CanUserAccessFundingAgreement(
            applicantUserId: null,
            isAdministrator: true,
            isReviewerAssignedToThisApplication: false);

        Assert.That(can, Is.True);
    }

    [Test]
    public void CanUserAccess_AssignedReviewer_ReturnsTrue()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());

        var can = application.CanUserAccessFundingAgreement(
            applicantUserId: null,
            isAdministrator: false,
            isReviewerAssignedToThisApplication: true);

        Assert.That(can, Is.True);
    }

    [Test]
    public void CanUserGenerate_Applicant_AlwaysFalse()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());

        Assert.That(
            application.CanUserGenerateFundingAgreement(
                isAdministrator: false,
                isReviewerAssignedToThisApplication: false),
            Is.False);
    }

    [Test]
    public void CanUserGenerate_Administrator_True()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());

        Assert.That(
            application.CanUserGenerateFundingAgreement(
                isAdministrator: true,
                isReviewerAssignedToThisApplication: false),
            Is.True);
    }

    [Test]
    public void CanUserGenerate_AssignedReviewer_True()
    {
        var application = BuildFinalizedApplication(acceptItemIds: new[] { 1 }, rejectItemIds: Array.Empty<int>());

        Assert.That(
            application.CanUserGenerateFundingAgreement(
                isAdministrator: false,
                isReviewerAssignedToThisApplication: true),
            Is.True);
    }

    private static AppEntity BuildFinalizedApplication(int[] acceptItemIds, int[] rejectItemIds)
    {
        var application = new AppEntity(applicantId: 1, 1, companyName: "Test Company");
        foreach (var id in acceptItemIds.Concat(rejectItemIds))
        {
            AddItem(application, id);
        }

        ApplicationResponseTransitionsTests.SetState(application, ApplicationState.Resolved);

        var decisions = new Dictionary<int, ItemResponseDecision>();
        foreach (var id in acceptItemIds) decisions[id] = ItemResponseDecision.Accept;
        foreach (var id in rejectItemIds) decisions[id] = ItemResponseDecision.Reject;

        if (decisions.Count > 0)
        {
            application.SubmitResponse(decisions, "applicant-user");
        }
        else
        {
            ApplicationResponseTransitionsTests.SetState(application, ApplicationState.ResponseFinalized);
        }

        return application;
    }

    private static void AddItem(AppEntity application, int itemId)
    {
        var item = new Item("p", 1, "specs");
        typeof(Item).GetProperty("Id")!.SetValue(item, itemId);
        application.AddItem(item);
    }

    private static void SetApplicantUserId(AppEntity application, string userId)
    {
        var applicant = new Applicant(
            userId: userId,
            legalId: "LEGAL-1",
            firstName: "Test",
            lastName: "Applicant",
            email: "applicant@test",
            phone: null,
            performanceScore: null);

        typeof(AppEntity).GetProperty("Applicant")!.SetValue(application, applicant);
    }
}
