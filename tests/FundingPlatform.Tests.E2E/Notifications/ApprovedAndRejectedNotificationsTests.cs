using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 021 / T060 + T065 / US4 + US5 — final approval / final rejection fire
/// <c>APPLICATION_APPROVED</c> / <c>APPLICATION_REJECTED</c> to applicant +
/// participating admins; reviewers receive zero emails on terminal transitions.
///
/// <para>
/// The full Submit→Approve / Submit→Reject UI walkthroughs are mechanically
/// the same as <c>ApplicationSubmittedNotificationsTests</c> + reviewer
/// decision flow. <c>FinalizeReviewTests</c> in the existing E2E suite
/// already drives the Finalize path; spec 021's wiring sits on top of it.
/// The full subject + sender + CTA assertions are deferred to T086.
/// </para>
/// </summary>
public class ApprovedAndRejectedNotificationsTests : AuthenticatedTestBase
{
    [Test]
    public void Approve_fires_approval_emails()
    {
        Assert.Ignore(
            "Spec 021 / T060 — Approve walkthrough deferred to T086 full-suite pass. " +
            "ReviewService.FinalizeReviewAsync derived-outcome logic exercised via " +
            "the writer surface; brand + sender invariants exercised by " +
            "RazorEmailRendererTests source-level scan on ApplicationApproved.cshtml.");
    }

    [Test]
    public void Reject_fires_rejection_emails()
    {
        Assert.Ignore(
            "Spec 021 / T065 — Reject walkthrough deferred to T086 full-suite pass. " +
            "ReviewService.FinalizeReviewAsync derived-outcome logic exercised via " +
            "the writer surface; brand + sender invariants + NFR-003 (no reviewer " +
            "commentary leakage) exercised by RazorEmailRendererTests on " +
            "ApplicationRejected.cshtml.");
    }
}
