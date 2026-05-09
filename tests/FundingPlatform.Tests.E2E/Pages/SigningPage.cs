using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Pages;

/// <summary>
/// Spec 019 T037 / FR-032 — Page Object Model for the applicant signing surface.
/// Walks the signing-ceremony entry view with semantic locators.
/// </summary>
public class SigningPage
{
    private readonly IPage _page;

    public SigningPage(IPage page)
    {
        _page = page;
    }

    public ILocator BrandSidebar => _page.Locator("[data-testid=\"sidebar-brand\"]");
    public ILocator SponsorStrip => _page.Locator("[data-testid=\"sponsor-strip\"]");
    public ILocator SignButton =>
        _page.GetByRole(AriaRole.Button, new() { NameRegex = new("(?i)firmar|sign") });
    public ILocator AwaitingCallout => _page.Locator(".fl-awaiting").First;
    public ILocator CeremonySeal => _page.Locator(".fl-ceremony-seal");
    public ILocator CeremonyHeadline => _page.Locator(".fl-ceremony-headline");
    public ILocator ConfettiCanvas => _page.Locator(".fl-confetti-canvas");
}
