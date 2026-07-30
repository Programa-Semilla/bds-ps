using FundingPlatform.Tests.E2E.Fixtures;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// The Ybug feedback widget (Views/Shared/_YbugWidget.cshtml) is a temporary
/// third-party script that must render ONLY on deployed environments. It is gated
/// on two conditions — environment != Development AND a configured <c>Ybug:Id</c> —
/// and neither holds under the E2E AspireFixture.
///
/// This test is the regression guard for that: it asserts the widget is absent from
/// both layouts, so no future change (a stray Ybug__Id in test config, a flipped
/// environment name, or the gate being dropped from the partial) can silently ship
/// a third-party script into the E2E run.
/// </summary>
public class YbugWidgetAbsentTests : AuthenticatedTestBase
{
    private HttpClient NewClient()
    {
        // The E2E BaseUrl is http; the app issues an HTTPS redirect. Follow it so we
        // assert on the rendered page, not the redirect body.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = true,
        };
        return new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
    }

    /// <summary>Landing page — renders through <c>_Layout</c> (via _ViewStart).</summary>
    [Test]
    public async Task MainLayout_DoesNotRenderYbugWidget()
    {
        using var client = NewClient();
        var html = await client.GetStringAsync("/");

        AssertNoYbug(html, "_Layout (landing page)");
    }

    /// <summary>Sign-in page — renders through <c>_AuthLayout</c>.</summary>
    [Test]
    public async Task AuthLayout_DoesNotRenderYbugWidget()
    {
        using var client = NewClient();
        var html = await client.GetStringAsync("/Account/Login");

        AssertNoYbug(html, "_AuthLayout (sign-in page)");
    }

    private static void AssertNoYbug(string html, string surface)
    {
        Assert.Multiple(() =>
        {
            // Positive control FIRST. A bare "does not contain" assertion also passes on
            // an error page or an empty body, which would make this test worthless. Both
            // layouts emit confirm-dialog.js immediately before the _YbugWidget include,
            // so asserting it (plus the closing body tag) proves we fetched the real
            // rendered layout AND that execution reached the widget's call site.
            Assert.That(html, Does.Contain("confirm-dialog.js"),
                $"{surface} did not render the expected layout — the Ybug assertion below "
                + "would be vacuous. Fix the fetch before trusting this test.");
            Assert.That(html, Does.Contain("</body>"),
                $"{surface} response is not a complete rendered page.");

            Assert.That(html, Does.Not.Contain("ybug"),
                $"{surface} must not contain the Ybug widget under E2E "
                + "(deployed-only: environment != Development AND Ybug:Id configured).");
            Assert.That(html, Does.Not.Contain("widget.ybug.io"),
                $"{surface} must not load the third-party Ybug script under E2E.");
        });
    }
}
