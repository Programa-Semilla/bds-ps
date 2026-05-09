using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T073 / FR-026 / spec US5 — All 9 empty-state illustrations render
/// with teal strokes (replacing the spec-011 forest-green strokes).
/// Asserts the SVG content exposes the new teal palette and not the legacy hex.
/// </summary>
public class EmptyStateIllustrationTests : AuthenticatedTestBase
{
    private static readonly string[] IllustrationFiles =
    {
        "calm-horizon.svg",
        "connected-nodes.svg",
        "folders-stack.svg",
        "gentle-disconnected-wires.svg",
        "magnifier-on-empty.svg",
        "off-center-compass.svg",
        "open-envelope.svg",
        "seed.svg",
        "soft-bar-chart.svg",
    };

    [Test]
    public async Task EveryIllustration_FetchesWithTealStrokes_AndNoForestGreenLegacyHex()
    {
        foreach (var name in IllustrationFiles)
        {
            var resp = await Page.GotoAsync($"{BaseUrl}/lib/illustrations/{name}");
            Assert.That(resp, Is.Not.Null, $"GET /lib/illustrations/{name} returned null response.");
            Assert.That(resp!.Status, Is.EqualTo(200), $"GET /lib/illustrations/{name} did not return 200.");
            var body = await resp.TextAsync();
            // Spec 019 SC-001 — legacy spec-011 hex must be absent from every illustration.
            Assert.That(body, Does.Not.Contain("#2E5E4E"),
                $"{name} still contains legacy forest-green stroke hex.");
            Assert.That(body, Does.Not.Contain("#1F4438"),
                $"{name} still contains legacy forest-green-strong stroke hex.");
            Assert.That(body, Does.Not.Contain("#D98A1B"),
                $"{name} still contains legacy amber accent hex.");
        }
    }
}
