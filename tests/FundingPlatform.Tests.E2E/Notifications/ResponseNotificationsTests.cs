using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 028 / US1 / T014 / SC-001 + SC-007 — driving the real UI, when the applicant
/// submits their response to the resolution the stage-group reviewer receives
/// <c>RESPONSE_SUBMITTED_REVIEWER</c> (CTA <c>/Review/{id}</c>) and the applicant
/// receives nothing. This closes the reported bug (reviewer not notified on accept).
/// </summary>
public class ResponseNotificationsTests : PostResolutionNotificationsE2EBase
{
    [Test]
    public async Task Applicant_response_notifies_reviewer_only()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 028 / NFR-007 — smtp4dev sidecar not available.");
            return;
        }

        var (appId, itemId) = await DriveToResolvedAsync(rejectItem: false);

        // Drain the submit/finalize mail so we only observe the response event.
        await MailCapture.DrainAsync();

        await LoginAsync(Page, ApplicantEmail, Password);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.AcceptRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();

        var messages = await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("El solicitante respondió la resolución"));

        var reviewerMsg = messages.FirstOrDefault(m =>
            m.ToAddresses.Any(t => t.Contains(ReviewerEmail, StringComparison.OrdinalIgnoreCase)));
        Assert.That(reviewerMsg, Is.Not.Null, "the stage-group reviewer must receive RESPONSE_SUBMITTED_REVIEWER.");

        Assert.Multiple(() =>
        {
            Assert.That(reviewerMsg!.Subject, Does.Contain($"Solicitud #{appId}"));
            Assert.That(reviewerMsg.HtmlBody + reviewerMsg.TextBody, Does.Contain($"/Review/{appId}"),
                "CTA must deep-link to the reviewer detail page.");
            Assert.That(reviewerMsg.FromDisplayName + reviewerMsg.FromAddress, Does.Contain("Programa Semilla"));
            Assert.That(reviewerMsg.HtmlBody, Does.Not.Contain("<img"), "NFR-001: no inline image.");
            Assert.That(reviewerMsg.HtmlBody + reviewerMsg.TextBody, Does.Not.Contain("Capital Semilla"));
            Assert.That(reviewerMsg.HtmlBody + reviewerMsg.TextBody, Does.Not.Contain("Forge"));
        });

        // The applicant (the actor) must receive nothing for this event.
        var applicantMsgs = messages.Where(m =>
            m.ToAddresses.Any(t => t.Contains(ApplicantEmail, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.That(applicantMsgs, Is.Empty,
            "SC-007: the applicant must NOT receive a copy of their own response.");
    }
}
