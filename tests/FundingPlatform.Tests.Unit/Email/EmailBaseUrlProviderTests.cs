// Spec 041 bugfix — EmailBaseUrlProvider must resolve email image/CTA base URLs to
// the SAME host as the working account links: trust a configured absolute base
// outside Development, otherwise use the live request host, and fall back to config
// when no request is in scope (the dispatch / stage-reminder workers).

using FundingPlatform.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FundingPlatform.Tests.Unit.Email;

[TestFixture]
public class EmailBaseUrlProviderTests
{
    private sealed class StubEnv(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(string? baseUrl) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Notifications:BaseUrl"] = baseUrl }).Build();

    private static IHttpContextAccessor Request(string? scheme, string? host)
    {
        var accessor = new HttpContextAccessor();
        if (scheme is not null && host is not null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = scheme;
            ctx.Request.Host = new HostString(host);
            accessor.HttpContext = ctx;
        }
        return accessor;
    }

    [Test]
    public void Production_with_valid_config_uses_config_and_trims_slash()
    {
        var sut = new EmailBaseUrlProvider(
            Request("https", "request-host.example"),
            new StubEnv("Production"),
            Config("https://configured.example/"));

        // Config wins outside Development even when a request host is present.
        Assert.That(sut.GetBaseUrl(), Is.EqualTo("https://configured.example"));
    }

    [Test]
    public void Development_ignores_config_and_uses_request_host()
    {
        var sut = new EmailBaseUrlProvider(
            Request("https", "localhost:7080"),
            new StubEnv("Development"),
            Config("http://localhost:5000")); // stale dev value that caused the bug

        Assert.That(sut.GetBaseUrl(), Is.EqualTo("https://localhost:7080"));
    }

    [Test]
    public void NonDevelopment_with_invalid_config_falls_back_to_request_host()
    {
        var sut = new EmailBaseUrlProvider(
            Request("https", "app.example"),
            new StubEnv("Staging"),
            Config("")); // not an absolute URL

        Assert.That(sut.GetBaseUrl(), Is.EqualTo("https://app.example"));
    }

    [Test]
    public void Worker_without_request_uses_config()
    {
        // No HttpContext (background dispatch / stage-reminder worker).
        var sut = new EmailBaseUrlProvider(
            Request(null, null),
            new StubEnv("Production"),
            Config("https://prod.example"));

        Assert.That(sut.GetBaseUrl(), Is.EqualTo("https://prod.example"));
    }

    [Test]
    public void Worker_in_development_without_request_returns_config_value()
    {
        // Edge: a worker in Development has no request; config is all it has.
        var sut = new EmailBaseUrlProvider(
            Request(null, null),
            new StubEnv("Development"),
            Config("http://localhost:5000"));

        Assert.That(sut.GetBaseUrl(), Is.EqualTo("http://localhost:5000"));
    }
}
