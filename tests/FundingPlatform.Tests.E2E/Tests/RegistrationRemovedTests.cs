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
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            AllowAutoRedirect = false,
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
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "POST to the removed registration endpoint must 404 (no account created).");
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
