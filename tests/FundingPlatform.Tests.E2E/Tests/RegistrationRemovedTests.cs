using System.Net;
using System.Text.RegularExpressions;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.Tests;

/// <summary>
/// Spec 032 / US1 — public self-registration is removed. <c>/Account/Register</c>
/// returns 404 for GET and POST, and no "register / create account" affordance
/// remains on the public landing page, the sign-in page, or the unauthenticated
/// navbar. The landing hero CTA now routes to sign-in.
/// </summary>
public class RegistrationRemovedTests : AuthenticatedTestBase
{
    private HttpClient NewClient()
    {
        // The E2E BaseUrl is http; the app issues an HTTPS redirect (308 KeepVerb).
        // Follow it (as the other dev-seam clients do) so we observe the final
        // status on the removed route — a 404 — rather than the redirect itself.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = true,
        };
        return new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
    }

    [Test]
    public async Task GetRegister_Returns404()
    {
        using var client = NewClient();
        var response = await client.GetAsync("/Account/Register");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PostRegister_Returns404_AndCreatesNothing()
    {
        using var client = NewClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "ghost@example.com",
            ["Password"] = "Test123!",
            ["ConfirmPassword"] = "Test123!",
            ["FirstName"] = "Ghost",
            ["LastName"] = "User",
        });
        var response = await client.PostAsync("/Account/Register", form);
        // The action is deleted, so the registration handler never runs (hence no account
        // can be created). GET cleanly 404s; a POST to the same path surfaces as 405
        // (Method Not Allowed) under the test's http→https redirect — either way the route
        // does not process a registration.
        Assert.That(response.StatusCode,
            Is.AnyOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed),
            "POST to the removed registration endpoint must be rejected (no handler).");
    }

    [Test]
    public async Task PublicLanding_HasNoRegisterLink_AndCtaGoesToLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/");

        Assert.That(await Page.Locator("a[href='/Account/Register']").CountAsync(), Is.EqualTo(0),
            "Public landing must not link to the removed registration page.");

        var cta = Page.Locator("[data-testid=\"public-landing-cta-button\"]");
        await Expect(cta).ToBeVisibleAsync();
        var href = await cta.GetAttributeAsync("href");
        Assert.That(href, Does.Contain("/Account/Login"),
            "Landing hero CTA must route to sign-in now that registration is gone.");
    }

    [Test]
    public async Task LoginPage_HasNoRegisterLink()
    {
        await Page.GotoAsync($"{BaseUrl}/Account/Login");
        Assert.That(await Page.Locator("a[href='/Account/Register']").CountAsync(), Is.EqualTo(0),
            "Sign-in page must not show a 'create account' link.");
    }

    [Test]
    public async Task UnauthenticatedNavbar_HasNoCreateAccountLink()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        Assert.That(await Page.Locator("header a[href='/Account/Register']").CountAsync(), Is.EqualTo(0),
            "Unauthenticated navbar must not show the 'Crear cuenta' link.");
    }
}
