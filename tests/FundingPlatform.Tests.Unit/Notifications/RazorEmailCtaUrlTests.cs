using FundingPlatform.Application.Notifications;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Web.Services;

namespace FundingPlatform.Tests.Unit.Notifications;

/// <summary>
/// Spec 021 / US9 / FR-040 — CTA deep-link composition. The withdrawal variant
/// must link to the reviewer queue (<c>/Review</c>) and NOT <c>/Review/{id}</c>:
/// the Application is soft-deleted, so the detail route would 403/404.
/// </summary>
[TestFixture]
public class RazorEmailCtaUrlTests
{
    private const string Base = "https://app.example.test";

    [Test]
    public void Withdrawal_links_to_reviewer_queue_not_detail()
    {
        var url = RazorEmailRenderer.ComposeCtaUrl(
            NotificationEvent.WithdrawnByApplicant, RecipientBucket.Reviewer, Base, applicationId: 42);

        Assert.That(url, Is.EqualTo($"{Base}/Review"));
        Assert.That(url, Does.Not.Contain("/Review/42"));
    }

    [Test]
    public void ReviewerSubmitted_links_to_application_detail_in_queue()
    {
        var url = RazorEmailRenderer.ComposeCtaUrl(
            NotificationEvent.ApplicationSubmittedReviewer, RecipientBucket.Reviewer, Base, applicationId: 42);

        Assert.That(url, Is.EqualTo($"{Base}/Review/42"));
    }

    [Test]
    public void ApplicantBucket_links_to_application_details()
    {
        var url = RazorEmailRenderer.ComposeCtaUrl(
            NotificationEvent.ApplicationSubmittedApplicant, RecipientBucket.Applicant, Base, applicationId: 42);

        Assert.That(url, Is.EqualTo($"{Base}/Application/Details/42"));
    }
}
