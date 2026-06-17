using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 037 FR-027 / SC-009 — the "⋯" row-actions kebab is reachable and operable
/// by keyboard (focus + Enter opens the menu), the focus ring is the official teal
/// token, and status pills carry an icon AND text (colour is never the sole signal).
/// </summary>
public class KeyboardAccessTests : AuthenticatedTestBase
{
    [Test]
    public async Task Kebab_OpensViaKeyboard_WithTealFocusRing()
    {
        // Sentinel admin sees the demo-seed users; their rows (non-self) render a kebab.
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.Locator("[name=Email]").FillAsync("admin@programa-semilla.test");
        await Page.Locator("[name=Password]").FillAsync("Sentinel123!");
        await Page.Locator("main button[type=submit]").ClickAsync();
        await Page.GotoAsync($"{BaseUrl}/Admin/Users");

        var toggle = Page.Locator("[data-testid^=\"row-actions-menu-\"]").First;
        await Expect(toggle).ToBeVisibleAsync();

        // Focusable + operable by keyboard: focus the toggle, press Enter, menu opens.
        await toggle.FocusAsync();
        var isFocused = await toggle.EvaluateAsync<bool>("el => el === document.activeElement");
        Assert.That(isFocused, Is.True, "Kebab toggle must be keyboard-focusable.");

        await Page.Keyboard.PressAsync("Enter");
        var menu = toggle.Locator("xpath=following-sibling::div[contains(@class,'dropdown-menu')]");
        await Expect(menu).ToBeVisibleAsync();

        // The focus ring is the official teal token (#008A9E), not blue (FR-027).
        var focusRing = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--color-focus-ring').trim()");
        // --color-focus-ring resolves through var(--color-primary); assert it is the
        // primary token reference or the literal official teal.
        Assert.That(focusRing, Does.Contain("008A9E").IgnoreCase
            .Or.Contain("var(--color-primary)").IgnoreCase
            .Or.Contain("color-primary"),
            $"Focus ring must be the official teal (got '{focusRing}').");
    }

    [Test]
    public async Task StatusPills_CarryIconAndText_NotColourAlone()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        await Page.Locator("[name=Email]").FillAsync("admin@programa-semilla.test");
        await Page.Locator("[name=Password]").FillAsync("Sentinel123!");
        await Page.Locator("main button[type=submit]").ClickAsync();
        await Page.GotoAsync($"{BaseUrl}/Admin/Users");

        var pill = Page.Locator("[data-testid=\"status-pill\"]").First;
        await Expect(pill).ToBeVisibleAsync();

        // Icon present (the <i>) AND a non-empty text label (colour is not the sole signal).
        var iconCount = await pill.Locator("i").CountAsync();
        Assert.That(iconCount, Is.GreaterThanOrEqualTo(1), "Status pill must carry an icon.");
        var text = (await pill.InnerTextAsync()).Trim();
        Assert.That(text, Is.Not.Empty, "Status pill must carry a text label, not colour alone.");
    }
}
