// Spec 033 / FR-002 / FR-011 — the emailed invitation is the headline artifact;
// assert its subject (es-CR), the embedded set-password link, the 72h expiry copy,
// and that the free-text name is HTML-encoded. A real template is written to a
// temp ContentRootPath so the factory's read+substitute+encode path is exercised
// deterministically (not the fallback body).

using System.Globalization;
using FundingPlatform.Infrastructure.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Unit.Email;

[TestFixture]
public class InvitationEmailFactoryTests
{
    private string _contentRoot = null!;

    private static readonly DateTimeOffset Expiry =
        new(2026, 6, 15, 18, 30, 0, TimeSpan.Zero);

    [SetUp]
    public void Setup()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "invite-email-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(_contentRoot, "Views", "Emails", "Identity");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "InvitationEmail.cshtml"),
            "<p>Hola {{FirstName}},</p><p><a href=\"{{InviteLink}}\">{{InviteLink}}</a></p>" +
            "<p>El enlace expira el {{ExpiresAt}}.</p>");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_contentRoot, recursive: true); } catch { /* best effort */ }
    }

    private InvitationEmailFactory BuildFactory() =>
        new(new TestHostEnvironment { ContentRootPath = _contentRoot },
            NullLogger<InvitationEmailFactory>.Instance);

    [Test]
    public void Build_SetsEsCrSubject()
    {
        var msg = BuildFactory().Build(
            "nuevo@programa-semilla.test", "Ana",
            "https://app.example/Account/ResetPassword?userId=u1&token=t1", Expiry);

        Assert.That(msg.ToAddress, Is.EqualTo("nuevo@programa-semilla.test"));
        Assert.That(msg.Subject, Is.EqualTo("Le han creado una cuenta — establezca su contraseña"));
    }

    [Test]
    public void Build_BodyContainsInviteLinkAndExpiry()
    {
        const string link = "https://app.example/Account/ResetPassword?userId=u1&token=abc%2Bdef";
        var msg = BuildFactory().Build("nuevo@programa-semilla.test", "Ana", link, Expiry);

        Assert.That(msg.HtmlBody, Does.Contain(link), "Body must carry the set-password link.");

        // Expiry is rendered in CR local time (UTC-6), es-CR formatted.
        var expectedExpiry = Expiry.ToOffset(TimeSpan.FromHours(-6))
            .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));
        Assert.That(msg.HtmlBody, Does.Contain(expectedExpiry), "Body must state the 72h expiry in CR local time.");
    }

    [Test]
    public void Build_HtmlEncodesFirstName()
    {
        var msg = BuildFactory().Build(
            "nuevo@programa-semilla.test", "<b>Eve</b>",
            "https://app.example/Account/ResetPassword?userId=u1&token=t1", Expiry);

        Assert.That(msg.HtmlBody, Does.Not.Contain("<b>Eve</b>"),
            "Free-text name must not be injected as raw HTML.");
        Assert.That(msg.HtmlBody, Does.Contain("&lt;b&gt;Eve&lt;/b&gt;"),
            "Free-text name must be HTML-encoded.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "FundingPlatform.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
