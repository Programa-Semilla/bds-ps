// Spec 033 / FR-002 + Spec 041 / T017 — the invitation email is the headline
// onboarding artifact. Spec 041 routes it through the shared branded shell via
// IEmailViewRenderer, so the factory's responsibility is now MODEL-BUILDING:
// assert the es-CR subject, the chosen views, and that the DirectEmailModel carries
// the invite link (CTA), the ALIA hero, the recipient name, and the 72h expiry copy.
// (HTML-encoding of the name + comment-stripping are now Razor's job, asserted by
// the live mail-capture E2E.)

using System.Globalization;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundingPlatform.Tests.Unit.Email;

[TestFixture]
public class InvitationEmailFactoryTests
{
    private static readonly DateTimeOffset Expiry =
        new(2026, 6, 15, 18, 30, 0, TimeSpan.Zero);

    /// <summary>Captures the (viewPath, model, disableLayout) of each render call.</summary>
    private sealed class CapturingRenderer : IEmailViewRenderer
    {
        public List<(string Path, object Model, bool DisableLayout)> Calls { get; } = new();
        public Task<string> RenderViewAsync(string viewPath, object model, bool disableLayout, CancellationToken ct)
        {
            Calls.Add((viewPath, model, disableLayout));
            return Task.FromResult(disableLayout ? "TEXT-BODY" : "HTML-BODY");
        }
    }

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Notifications:BaseUrl"] = "https://app.example" }).Build();

    private static InvitationEmailFactory Build(CapturingRenderer r) =>
        new(r, Config(), NullLogger<InvitationEmailFactory>.Instance);

    [Test]
    public async Task BuildAsync_SetsEsCrSubject_AndRendersHtmlPlusText()
    {
        var r = new CapturingRenderer();
        var msg = await Build(r).BuildAsync(
            "nuevo@programa-semilla.test", "Ana",
            "https://app.example/Account/ResetPassword?userId=u1&token=t1", Expiry);

        Assert.That(msg.ToAddress, Is.EqualTo("nuevo@programa-semilla.test"));
        Assert.That(msg.Subject, Is.EqualTo("Le han creado una cuenta — establezca su contraseña"));
        Assert.That(msg.HtmlBody, Is.EqualTo("HTML-BODY"));
        Assert.That(msg.TextBody, Is.EqualTo("TEXT-BODY"));
        Assert.That(r.Calls, Has.Count.EqualTo(2));
        Assert.That(r.Calls[0].Path, Does.Contain("Identity/InvitationEmail.cshtml"));
        Assert.That(r.Calls[0].DisableLayout, Is.False);
        Assert.That(r.Calls[1].Path, Does.Contain("Identity/InvitationEmail.text.cshtml"));
        Assert.That(r.Calls[1].DisableLayout, Is.True);
    }

    [Test]
    public async Task BuildAsync_ModelCarriesInviteLinkHeroNameAndExpiry()
    {
        const string link = "https://app.example/Account/ResetPassword?userId=u1&token=abc%2Bdef";
        var r = new CapturingRenderer();
        await Build(r).BuildAsync("nuevo@programa-semilla.test", "Ana", link, Expiry);

        var model = (DirectEmailModel)r.Calls[0].Model;
        Assert.That(model.CtaUrl, Is.EqualTo(link), "CTA must carry the set-password link unchanged.");
        Assert.That(model.HeroTitle, Is.EqualTo("Bienvenida a ALIA"));
        Assert.That(model.DisplayName, Is.EqualTo("Ana"));

        var expectedExpiry = Expiry.ToOffset(TimeSpan.FromHours(-6))
            .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));
        Assert.That(model.FooterNote, Does.Contain(expectedExpiry),
            "Footer note must state the 72h expiry in CR local time.");
        Assert.That(model.LogoUrl, Does.Contain("/lib/brand/programa-semilla-horizontal.png"));
        Assert.That(model.PartnerStripUrl, Does.Contain("/lib/brand/partners-footer.png"));
    }
}
