using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T070 / FR-034 / SC-010 / spec US4 #2 — Reduced-motion contract:
/// confetti is suppressed and a static teal-branded card renders.
/// </summary>
public class ReducedMotionTests : AuthenticatedTestBase
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ReducedMotion = ReducedMotion.Reduce,
    };

    [Test]
    public async Task ReducedMotion_SetsMotionTokensToZero()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        var motionDuration = await Page.EvaluateAsync<string>(@"
            () => getComputedStyle(document.documentElement).getPropertyValue('--motion-celebratory').trim()
        ");
        Assert.That(motionDuration, Is.EqualTo("0ms"),
            "--motion-celebratory MUST clamp to 0ms under prefers-reduced-motion: reduce.");

        // --motion-opacity-exempt remains 150ms per the spec 011 contract preserved
        // verbatim by FR-017.
        var opacityExempt = await Page.EvaluateAsync<string>(@"
            () => getComputedStyle(document.documentElement).getPropertyValue('--motion-opacity-exempt').trim()
        ");
        Assert.That(opacityExempt, Is.EqualTo("150ms"),
            "--motion-opacity-exempt MUST remain 150ms under reduced-motion (FR-017 contract).");
    }
}
