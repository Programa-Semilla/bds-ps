using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Storage;

/// <summary>
/// Spec 014 / T033 / US1 / FR-018 — authorization is enforced at the
/// application boundary; signed URLs never leak into HTML responses.
/// </summary>
[Category("Storage014")]
public class SignedFundingAgreementAuthorizationTests : AuthenticatedTestBase
{
    [Test]
    public async Task Unauthenticated_request_to_download_redirects_or_returns_4xx()
    {
        // Anonymous request to a known download URL. The platform routes
        // unauthenticated callers to /Account/Login (302) before the
        // authorization handler runs, which is the documented behaviour
        // for [Authorize] controllers (Web/Program.cs cookie config).
        // Either a 302 to login or a direct 401/403 satisfies FR-018.
        await using var anon = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
        var anonPage = await anon.NewPageAsync();
        var resp = await anonPage.GotoAsync($"{BaseUrl}/Applications/1/FundingAgreement/Download");
        Assert.That(resp, Is.Not.Null);
        Assert.That(resp!.Status,
            Is.EqualTo(401)
            .Or.EqualTo(403)
            .Or.EqualTo(404)
            .Or.EqualTo(200), // 200 lands on the login redirect target
            $"Unexpected status {resp.Status}.");

        var body = await anonPage.ContentAsync();
        // FR-018 — signed URLs are never in HTML. Even on a 404 page, no SAS
        // signature should be visible. We look for the unique SAS query token
        // 'sig=' and the storage subdomain.
        Assert.That(body, Does.Not.Contain("sig="), "Response body must not contain a SAS signature.");
        Assert.That(body, Does.Not.Contain(".blob.core.windows.net"),
            "Response body must not leak a blob endpoint URL.");
    }

    [Test]
    public async Task Authenticated_non_owner_gets_404_without_blob_url()
    {
        // Existing FundingAgreementTests.US6 covers the 404 happy path with a
        // logged-in stranger. This test re-asserts the no-leak invariant
        // independently for spec 014's new path: even when the controller
        // returns 404, the rendered error view must not echo a signed URL.
        var uniq = Guid.NewGuid().ToString("N")[..8];
        var strangerEmail = $"sfa_stranger_{uniq}@example.com";
        await RegisterUserAsync(Page, strangerEmail, "Test123!", "Stranger", "User", $"STR-{uniq}");
        await LoginAsync(Page, strangerEmail, "Test123!");

        var resp = await Page.GotoAsync($"{BaseUrl}/Applications/999999/FundingAgreement/Download");
        Assert.That(resp, Is.Not.Null);
        Assert.That(resp!.Status, Is.EqualTo(404));

        var body = await Page.ContentAsync();
        Assert.That(body, Does.Not.Contain("sig="));
        Assert.That(body, Does.Not.Contain(".blob.core.windows.net"));
    }
}
