using System.Reflection;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class ApplicationResponseTransitionsTests
{
    [Test]
    public void SubmitResponse_FromResolved_TransitionsToResponseFinalized()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1, 2 });

        var decisions = new Dictionary<int, ItemResponseDecision>
        {
            [1] = ItemResponseDecision.Accept,
            [2] = ItemResponseDecision.Reject
        };

        var response = application.SubmitResponse(decisions, "user-1");

        Assert.That(application.State, Is.EqualTo(ApplicationState.ResponseFinalized));
        Assert.That(response.CycleNumber, Is.EqualTo(1));
        Assert.That(application.ApplicantResponses, Has.Count.EqualTo(1));
        Assert.That(application.ApplicantResponses[0].ItemResponses, Has.Count.EqualTo(2));
    }

    [Test]
    public void SubmitResponse_FromDraft_Throws()
    {
        var application = new AppEntity(applicantId: 1, companyName: "Test Company");
        SetItem(application, 1);

        Assert.Throws<InvalidOperationException>(() =>
            application.SubmitResponse(
                new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Accept },
                "user-1"));
    }

    [Test]
    public void SubmitResponse_IncrementsCycleNumberOnSubsequentCycles()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Accept },
            "user-1");

        Assert.That(application.ApplicantResponses[0].CycleNumber, Is.EqualTo(1));

        SetState(application, ApplicationState.Resolved);

        var response2 = application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");

        Assert.That(response2.CycleNumber, Is.EqualTo(2));
        Assert.That(application.ApplicantResponses, Has.Count.EqualTo(2));
    }

    [Test]
    public void OpenAppeal_FromResponseFinalized_WithRejectedItem_TransitionsToAppealOpen()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1, 2 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision>
            {
                [1] = ItemResponseDecision.Accept,
                [2] = ItemResponseDecision.Reject
            },
            "user-1");

        var appeal = application.OpenAppeal("user-1", maxAppeals: 1);

        Assert.That(application.State, Is.EqualTo(ApplicationState.AppealOpen));
        Assert.That(appeal.Status, Is.EqualTo(AppealStatus.Open));
        Assert.That(application.Appeals, Has.Count.EqualTo(1));
    }

    [Test]
    public void OpenAppeal_FromDraft_Throws()
    {
        var application = new AppEntity(applicantId: 1, companyName: "Test Company");
        Assert.Throws<InvalidOperationException>(() => application.OpenAppeal("user-1", maxAppeals: 1));
    }

    [Test]
    public void OpenAppeal_WithNoRejectedItems_Throws()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Accept },
            "user-1");

        Assert.Throws<InvalidOperationException>(() => application.OpenAppeal("user-1", maxAppeals: 1));
    }

    [Test]
    public void OpenAppeal_AtCap_Throws()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");
        application.OpenAppeal("user-1", maxAppeals: 1);
        // Resolve and submit again to reset state, then try to open a second appeal
        application.ResolveAppealAsUphold("reviewer-1");

        Assert.Throws<InvalidOperationException>(() => application.OpenAppeal("user-1", maxAppeals: 1));
    }

    [Test]
    public void OpenAppeal_WithMaxAppealsZero_Throws()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");

        Assert.Throws<InvalidOperationException>(() => application.OpenAppeal("user-1", maxAppeals: 0));
    }

    [Test]
    public void ApplicantResponses_AreExposedInInsertionOrder()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Accept },
            "user-1");

        SetState(application, ApplicationState.Resolved);

        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");

        Assert.That(application.ApplicantResponses[0].CycleNumber, Is.EqualTo(1));
        Assert.That(application.ApplicantResponses[1].CycleNumber, Is.EqualTo(2));
    }

    [Test]
    public void ResolveAppealAsUphold_FromAppealOpen_TransitionsToResponseFinalized()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");
        application.OpenAppeal("user-1", maxAppeals: 1);

        application.ResolveAppealAsUphold("reviewer-1");

        Assert.That(application.State, Is.EqualTo(ApplicationState.ResponseFinalized));
        Assert.That(application.Appeals[0].Status, Is.EqualTo(AppealStatus.Resolved));
        Assert.That(application.Appeals[0].Resolution, Is.EqualTo(AppealResolution.Uphold));
    }

    [Test]
    public void ResolveAppealAsGrantReopenToDraft_TransitionsToDraft_AndClearsSubmittedAt()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");
        application.OpenAppeal("user-1", maxAppeals: 1);

        application.ResolveAppealAsGrantReopenToDraft("reviewer-1");

        Assert.That(application.State, Is.EqualTo(ApplicationState.Draft));
        Assert.That(application.SubmittedAt, Is.Null);
        Assert.That(application.Appeals[0].Resolution, Is.EqualTo(AppealResolution.GrantReopenToDraft));
    }

    [Test]
    public void ResolveAppealAsGrantReopenToReview_TransitionsToUnderReview_AndPreservesItemStatuses()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        // Item 1 is in Pending status from construction
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");
        application.OpenAppeal("user-1", maxAppeals: 1);

        var itemStatusBefore = application.Items[0].ReviewStatus;
        application.ResolveAppealAsGrantReopenToReview("reviewer-1");

        Assert.That(application.State, Is.EqualTo(ApplicationState.UnderReview));
        Assert.That(application.Items[0].ReviewStatus, Is.EqualTo(itemStatusBefore), "Item statuses must be preserved (not reset).");
        Assert.That(application.Appeals[0].Resolution, Is.EqualTo(AppealResolution.GrantReopenToReview));
    }

    [Test]
    public void ResolveAppealAs_FromWrongState_Throws()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });

        Assert.Throws<InvalidOperationException>(() => application.ResolveAppealAsUphold("reviewer-1"));
        Assert.Throws<InvalidOperationException>(() => application.ResolveAppealAsGrantReopenToDraft("reviewer-1"));
        Assert.Throws<InvalidOperationException>(() => application.ResolveAppealAsGrantReopenToReview("reviewer-1"));
    }

    [Test]
    public void AfterResolveAtCap_NewAppealStillForbidden()
    {
        var application = BuildResolvedApplication(itemIds: new[] { 1 });
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [1] = ItemResponseDecision.Reject },
            "user-1");
        application.OpenAppeal("user-1", maxAppeals: 1);
        application.ResolveAppealAsUphold("reviewer-1");

        Assert.Throws<InvalidOperationException>(() => application.OpenAppeal("user-1", maxAppeals: 1));
    }

    internal static AppEntity BuildResolvedApplication(int[] itemIds)
    {
        var application = new AppEntity(applicantId: 1, companyName: "Test Company");
        foreach (var id in itemIds)
        {
            SetItem(application, id);
        }
        SetState(application, ApplicationState.Resolved);
        return application;
    }

    internal static void SetState(AppEntity application, ApplicationState state)
    {
        typeof(AppEntity).GetProperty("State")!.SetValue(application, state);
    }

    private static void SetItem(AppEntity application, int itemId)
    {
        var item = new Item("p", 1, "specs");
        typeof(Item).GetProperty("Id")!.SetValue(item, itemId);
        application.AddItem(item);
    }
}
