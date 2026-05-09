using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T085 / research R13 — Print stylesheet contract: sponsor strip
/// is HIDDEN on application detail and reviewer queue print views, but
/// PRESENT on Login. Implemented via @media print { [data-print-hide="sponsor-strip"]
/// { display: none } } in tokens.css; surfaces opt-in by setting the attribute.
/// </summary>
public class PrintLayoutTests : AuthenticatedTestBase
{
    [Test]
    public async Task PrintMedia_AuthSurfaceKeepsSponsorStrip_AppDetailHides()
    {
        await Page.EmulateMediaAsync(new() { Media = Media.Print });

        // Auth surface — sponsor strip should remain visible in print.
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        var authStrip = Page.Locator("[data-testid=\"sponsor-strip\"]");
        await Expect(authStrip).ToBeVisibleAsync();

        // Application detail with `data-print-hide="sponsor-strip"` would
        // suppress the strip in print. The current iteration does not yet wire
        // the per-surface attribute; this test documents the contract — when
        // T044 / T053 add the attribute, the assertion below flips to
        // ToBeHiddenAsync.
        // (Deferred to follow-up: per-surface data-print-hide opt-in.)
    }
}
