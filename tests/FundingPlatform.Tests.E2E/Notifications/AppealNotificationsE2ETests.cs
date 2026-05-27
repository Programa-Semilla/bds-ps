using FundingPlatform.Tests.E2E.Constants;
using FundingPlatform.Tests.E2E.Fixtures;
using FundingPlatform.Tests.E2E.PageObjects;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 028 / US2 / T023 / SC-001 + SC-002 — appeal lifecycle through the real UI:
/// open → applicant message → reviewer reply → resolve as GrantReopenToReview.
/// Asserts directional captures (reviewer-bound vs applicant-bound) and the
/// dual-fire (APPEAL_RESOLVED_APPLICANT + APPEAL_REOPENED_REVIEWER). A second group
/// reviewer is registered so the reopen-reviewer email has a non-actor recipient
/// (the resolving reviewer is excluded — FR-013a).
/// </summary>
public class AppealNotificationsE2ETests : PostResolutionNotificationsE2EBase
{
    private static bool Has(IEnumerable<CapturedMessage> messages, string subjectPrefix, string email) =>
        messages.Any(m => m.Subject.StartsWith(subjectPrefix)
            && m.ToAddresses.Any(t => t.Contains(email, StringComparison.OrdinalIgnoreCase)));

    [Test]
    public async Task Appeal_lifecycle_fires_directional_and_dual_fire_notifications()
    {
        if (MailCapture is null)
        {
            Assert.Inconclusive("Spec 028 / NFR-007 — smtp4dev sidecar not available.");
            return;
        }

        var (appId, itemId) = await DriveToResolvedAsync(rejectItem: true);

        // A second group reviewer: recipient of the reopen-reviewer email, since the
        // resolving reviewer (ReviewerEmail) is the excluded actor.
        var reviewer2Email = $"pr_rev2_{UniqueId}@programa-semilla.test";
        await RegisterUserAsync(Page, reviewer2Email, Password, "Rodrigo", "Revisor", $"PRR2-{UniqueId}");
        await AssignRoleAsync(reviewer2Email, "Reviewer");

        await MailCapture.DrainAsync();

        // Applicant: reject the resolution, then open an appeal + post a message.
        await LoginAsync(Page, ApplicantEmail, Password);
        var responsePage = new ApplicantResponsePage(Page);
        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.RejectRadio(itemId).CheckAsync();
        await responsePage.SubmitAsync();
        await Expect(responsePage.SuccessMessage).ToBeVisibleAsync();

        await responsePage.GotoAsync(BaseUrl, appId);
        await responsePage.OpenAppealButton.ClickAsync();
        var appealPage = new AppealThreadPage(Page);
        await Expect(appealPage.AppealStatus).ToContainTextAsync(UiCopy.State.Open);

        await appealPage.PostMessageAsync("Solicito reconsiderar — el ítem sigue siendo necesario.");
        await Expect(appealPage.SuccessMessage).ToBeVisibleAsync();
        await LogoutAsync();

        // Reviewer: reply, then resolve as GrantReopenToReview (dual-fire).
        await LoginAsync(Page, ReviewerEmail, Password);
        await appealPage.GotoAsync(BaseUrl, appId);
        await appealPage.PostMessageAsync("Gracias por el detalle; reabrimos para una nueva revisión.");
        await Expect(appealPage.SuccessMessage).ToBeVisibleAsync();

        await appealPage.GotoAsync(BaseUrl, appId);
        await appealPage.GrantReopenReviewButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Review"));

        // The reopen-reviewer email is the last event in the walk — wait on it, then
        // assert the full directional set from a single listing.
        await MailCapture.WaitForAsync(
            minCount: 1, timeout: TimeSpan.FromSeconds(60),
            filter: m => m.Subject.StartsWith("Apelación concedida"));
        var all = await MailCapture.ListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Has(all, "Nueva apelación abierta", ReviewerEmail), Is.True,
                "APPEAL_OPENED_REVIEWER → reviewer.");
            Assert.That(Has(all, "Nuevo mensaje en la apelación", ReviewerEmail), Is.True,
                "APPEAL_MESSAGE_REVIEWER → reviewer (applicant authored).");
            Assert.That(Has(all, "Nuevo mensaje del revisor en tu apelación", ApplicantEmail), Is.True,
                "APPEAL_MESSAGE_APPLICANT → applicant (reviewer authored).");
            Assert.That(Has(all, "Resolución de tu apelación", ApplicantEmail), Is.True,
                "APPEAL_RESOLVED_APPLICANT → applicant.");
            Assert.That(Has(all, "Apelación concedida", reviewer2Email), Is.True,
                "APPEAL_REOPENED_REVIEWER → the non-actor group reviewer (dual-fire).");
            Assert.That(Has(all, "Apelación concedida", ReviewerEmail), Is.False,
                "FR-013a: the resolving reviewer (actor) is excluded from the reopen email.");
        });
    }
}
