using FundingPlatform.Web.Services.Emails;

namespace FundingPlatform.Tests.Unit.Notifications;

/// <summary>
/// Spec 041 / T012 — source-level invariants on the shared email design system
/// (shell + partials). Mirrors the house style of <see cref="RazorEmailRendererTests"/>
/// (grep the .cshtml + brand constants rather than bootstrapping the Razor engine;
/// real rendered output is asserted by the integration + E2E mail-capture suites).
/// </summary>
[TestFixture]
public class EmailDesignSystemTests
{
    private const string ViewsRelativePath = "src/FundingPlatform.Web/Views/Emails";

    private static string FindViewsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FundingPlatform.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException("Could not find solution root (FundingPlatform.slnx).");
        }
        return Path.Combine(dir.FullName, ViewsRelativePath);
    }

    private static string SharedPartial(string name) =>
        File.ReadAllText(Path.Combine(FindViewsRoot(), "Shared", $"{name}.cshtml"));

    [Test]
    public void Brand_copy_constants_are_locked()
    {
        // The reference copy + spec pin these exact values (FR-006/FR-007).
        Assert.That(EmailBrand.PlatformName, Is.EqualTo("ALIA"));
        Assert.That(EmailBrand.SignOff, Is.EqualTo("Equipo Programa Semilla"));
        Assert.That(EmailBrand.SupportPhone, Is.EqualTo("+506 4600-1234"));
        Assert.That(EmailBrand.PrimaryTeal, Is.EqualTo("#008a9e"));
    }

    [Test]
    public void CtaButton_is_guarded_by_url_and_always_emits_a_fallback_link()
    {
        // FR-005 — the button renders ONLY when a URL is supplied, and is always
        // followed by a plain-text fallback link referencing the same URL.
        var cta = SharedPartial("_CtaButton");
        Assert.That(cta, Does.Contain("IsNullOrWhiteSpace(Model.Url)"),
            "_CtaButton must guard rendering on a non-empty Url (FR-005).");
        // Fallback link: the partial references Model.Url at least twice (button href + fallback).
        var occurrences = cta.Split("@Model.Url").Length - 1;
        Assert.That(occurrences, Is.GreaterThanOrEqualTo(2),
            "_CtaButton must emit a plain-text fallback link in addition to the button (FR-005).");
    }

    [Test]
    public void No_near_black_button_anywhere_in_email_views()
    {
        // FR-003 — the old near-black #1d1d1f CTA/background is removed everywhere.
        var root = FindViewsRoot();
        foreach (var path in Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories))
        {
            var contents = File.ReadAllText(path);
            Assert.That(contents.ToLowerInvariant(), Does.Not.Contain("#1d1d1f"),
                $"FR-003: '{Path.GetFileName(path)}' still references the retired near-black #1d1d1f.");
        }
    }

    [Test]
    public void Partner_footer_carries_support_email_phone_and_automatic_note()
    {
        // FR-006 — every email's footer carries the partner strip + support email
        // + phone + automatic-message note. The partial sources these from EmailBrand.
        var footer = SharedPartial("_PartnerFooter");
        Assert.That(footer, Does.Contain("EmailBrand.SupportEmail"));
        Assert.That(footer, Does.Contain("EmailBrand.SupportPhone"));
        Assert.That(footer, Does.Contain("EmailBrand.AutomaticMessageNote"));
        Assert.That(footer, Does.Contain("<img"),
            "_PartnerFooter must render the partner-logo strip image (FR-006).");
    }

    [Test]
    public void Brand_header_renders_logo_with_spanish_alt()
    {
        // FR-002 / NFR-004 — hosted logo with a non-empty Spanish alt.
        var header = SharedPartial("_BrandHeader");
        Assert.That(header, Does.Contain("<img"));
        Assert.That(header, Does.Contain("alt=\"Programa Semilla\""),
            "_BrandHeader logo must carry Spanish alt text (NFR-004).");
    }

    private static IEnumerable<string> AllEmailViews()
    {
        var root = FindViewsRoot();
        return Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories);
    }

    private static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(
        s, @"@\*.*?\*@", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

    [Test]
    public void T037_no_external_or_embedded_css_in_any_email_view()
    {
        // NFR-001 — inline CSS only: no <link rel=stylesheet> and no <style> blocks.
        foreach (var path in AllEmailViews())
        {
            var contents = Strip(File.ReadAllText(path));
            Assert.That(contents, Does.Not.Contain("<link"),
                $"NFR-001: '{Path.GetFileName(path)}' references an external stylesheet.");
            Assert.That(contents, Does.Not.Contain("<style"),
                $"NFR-001: '{Path.GetFileName(path)}' embeds a <style> block (must be inline CSS).");
        }
    }

    [Test]
    public void T038_every_img_has_nonempty_alt_and_no_flexbox_or_grid()
    {
        // NFR-002/NFR-004 — no flexbox/grid (email-client compat); every <img> has alt.
        foreach (var path in AllEmailViews())
        {
            var contents = Strip(File.ReadAllText(path)).ToLowerInvariant();
            Assert.That(contents, Does.Not.Contain("display:flex"),
                $"NFR-002: '{Path.GetFileName(path)}' uses flexbox.");
            Assert.That(contents, Does.Not.Contain("display:grid"),
                $"NFR-002: '{Path.GetFileName(path)}' uses CSS grid.");

            // Each <img ...> tag must carry a non-empty alt="...".
            foreach (System.Text.RegularExpressions.Match img in
                     System.Text.RegularExpressions.Regex.Matches(contents, "<img\\b[^>]*>"))
            {
                var alt = System.Text.RegularExpressions.Regex.Match(img.Value, "alt=\"([^\"]*)\"");
                Assert.That(alt.Success && alt.Groups[1].Value.Trim().Length > 0, Is.True,
                    $"NFR-004: an <img> in '{Path.GetFileName(path)}' is missing a non-empty alt.");
            }
        }
    }

    [Test]
    public void T038_layout_caps_content_width_at_600px()
    {
        var root = FindViewsRoot();
        var layout = File.ReadAllText(Path.Combine(root, "_EmailLayout.cshtml"));
        Assert.That(layout, Does.Contain("max-width:600px"),
            "NFR-001: the content table must be capped at 600px.");
    }

    // The direct-send + notifier emails are NOT in the NotificationEvent enum, so the
    // outbox twin-parity test (RazorEmailRendererTests) doesn't cover them. Assert
    // their .text.cshtml twins exist on disk (FR-009 / SC-007) — a deleted twin would
    // otherwise only surface at E2E runtime (which is skipped without the sidecar).
    private static readonly string[] DirectSendViews =
    {
        "Identity/InvitationEmail",
        "Identity/ForgotPasswordEmail",
        "Identity/PasswordChangedEmail",
        "Stages/T24ReminderEmail",
        "Stages/T72ReminderEmail",
        "Stages/ExpiredEmail",
        "Suppliers/ProviderCreatedAuditor",
    };

    [Test]
    public void Direct_send_emails_have_html_and_text_twins()
    {
        var root = FindViewsRoot();
        foreach (var name in DirectSendViews)
        {
            var html = Path.Combine(root, $"{name}.cshtml");
            var text = Path.Combine(root, $"{name}.text.cshtml");
            Assert.That(File.Exists(html), Is.True, $"FR-009: missing HTML view {name}.cshtml");
            Assert.That(File.Exists(text), Is.True, $"FR-009: missing text twin {name}.text.cshtml");
        }
    }
}
