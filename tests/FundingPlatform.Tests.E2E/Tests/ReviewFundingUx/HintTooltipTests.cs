using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests.ReviewFundingUx;

/// <summary>
/// Spec 027 / US7 (SC-006) — applicant form fields carry an info icon whose
/// hover/focus reveals an HTML-capable es-CR tooltip (formatting rendered, not
/// escaped). Driven by the own-JS hint-tooltip module (no window.bootstrap).
/// </summary>
[Category("ReviewFundingUx")]
public class HintTooltipTests : AuthenticatedTestBase
{
    [Test]
    public async Task RegisterEmailField_HasInfoIcon_HoverRendersFormattedHtml()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Register");

        // The email field carries the info icon.
        var icon = Page.Locator("[data-field=Email] [data-hint]");
        await Expect(icon).ToBeVisibleAsync();
        await Expect(icon.Locator("i.ti-info-circle")).ToBeVisibleAsync();

        // Hovering shows the bubble with rendered HTML (a <strong>, not escaped tags).
        await icon.HoverAsync();
        var bubble = Page.Locator("#fl-hint-bubble");
        await Expect(bubble).ToBeVisibleAsync();
        Assert.That(await bubble.Locator("strong").CountAsync(), Is.GreaterThanOrEqualTo(1),
            "Tooltip copy must render HTML formatting, not escaped tags.");

        var text = await bubble.InnerTextAsync();
        Assert.That(text, Does.Not.Contain("<strong>"), "HTML must not appear as literal escaped text.");
        Assert.That(text, Does.Contain("notificaciones"));
    }
}
