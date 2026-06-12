// Spec 033 / FR-008 / C5 — Page Object for the "Invitación enviada" confirmation
// rendered from the admin create / resend POST.

using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects.Admin;

public class InvitationSentPage : AdminBasePage
{
    public InvitationSentPage(IPage page) : base(page)
    {
    }

    public ILocator Root => Page.Locator("[data-testid=\"invitation-sent\"]");
    public ILocator Headline => Page.Locator("[data-testid=\"invitation-sent-headline\"]");
    public ILocator InviteLinkInput => Page.Locator("[data-testid=\"invitation-link\"]");
    public ILocator CopyButton => Page.Locator("[data-testid=\"invitation-link-copy\"]");
    public ILocator BackToUsers => Page.Locator("[data-testid=\"invitation-back-to-users\"]");

    /// <summary>Returns the raw set-password invite link shown once on the confirmation.</summary>
    public Task<string> GetInviteLinkAsync() => InviteLinkInput.InputValueAsync();
}
