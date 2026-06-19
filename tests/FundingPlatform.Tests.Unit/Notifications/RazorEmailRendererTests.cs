using FundingPlatform.Application.Notifications.Templates;
using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Tests.Unit.Notifications;

/// <summary>
/// Spec 021 / T041 / NFR-001 / NFR-003 / FR-027 — source-level assertions on
/// every email template's <c>.cshtml</c> file. Catches the regressions that
/// matter without needing the Razor view engine bootstrapped in a unit test:
///
/// <list type="bullet">
///   <item>Every variant ships HTML + plain-text file pair (12 files total).</item>
///   <item>No inline <c>&lt;img&gt;</c> in any HTML body (NFR-001 / spec 019 NFR-005).</item>
///   <item>No <c>Capital Semilla</c> / <c>Forge</c> string anywhere (FR-027 / SC-006).</item>
///   <item>No English-only marker phrases.</item>
///   <item>HTML variants reference the model + CTA URL contract.</item>
///   <item>Rejection body never references reviewer-internal commentary verbatim
///         (NFR-003 — body links to the decision-detail page where access control
///         is enforced server-side).</item>
/// </list>
///
/// <para>
/// The integration- and E2E-level assertions on <em>rendered</em> output (sender
/// display, signature block, real recipient interpolation) live in
/// <c>RazorEmailRendererIntegrationTests</c> and the E2E suite (T043 / T081).
/// </para>
/// </summary>
[TestFixture]
public class RazorEmailRendererTests
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

    private static IEnumerable<string> AllVariantViewNames()
    {
        foreach (NotificationEvent ev in Enum.GetValues(typeof(NotificationEvent)))
        {
            yield return NotificationTemplateBindings.For(ev).HtmlViewName;
        }
    }

    [Test]
    public void Every_variant_has_html_and_text_files()
    {
        var root = FindViewsRoot();
        foreach (var name in AllVariantViewNames())
        {
            var html = Path.Combine(root, $"{name}.cshtml");
            var text = Path.Combine(root, $"{name}.text.cshtml");
            Assert.That(File.Exists(html), Is.True, $"Missing HTML view: {html}");
            Assert.That(File.Exists(text), Is.True, $"Missing text view: {text}");
        }
    }

    [Test]
    public void No_inline_img_in_html_bodies()
    {
        // Spec 041 reverses spec-021's blanket no-image rule: the branded shell
        // intentionally carries a hosted logo (_BrandHeader) + partner strip
        // (_PartnerFooter). The remaining invariant is that per-email BODY
        // templates carry no content-only <img> (NFR-004 image-blocked legibility)
        // — all imagery lives in the shared chrome partials.
        var root = FindViewsRoot();
        foreach (var name in AllVariantViewNames())
        {
            var path = Path.Combine(root, $"{name}.cshtml");
            var contents = StripRazorComments(File.ReadAllText(path));
            Assert.That(contents, Does.Not.Contain("<img"),
                $"Spec 041 / NFR-004: {name}.cshtml embeds a content <img> — imagery belongs in the shared chrome partials.");
        }
    }

    private static string StripRazorComments(string source) =>
        System.Text.RegularExpressions.Regex.Replace(
            source, @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

    [Test]
    public void Brand_grep_gate_finds_no_capital_semilla_or_forge_strings()
    {
        var root = FindViewsRoot();
        foreach (var path in Directory.EnumerateFiles(root, "*.cshtml"))
        {
            var contents = File.ReadAllText(path);
            Assert.That(contents, Does.Not.Contain("Capital Semilla"),
                $"FR-027 / SC-006: '{Path.GetFileName(path)}' contains 'Capital Semilla'.");
            Assert.That(contents, Does.Not.Contain("Forge"),
                $"FR-027 / SC-006: '{Path.GetFileName(path)}' contains 'Forge'.");
        }
    }

    [Test]
    public void Variants_use_es_cr_copy_no_english_markers()
    {
        // Marker words/phrases that would never appear in an es-CR template body.
        // Note: 'Email', 'Subject' as Razor-keyword identifiers (ViewData["Title"])
        // are fine in code; the source-string scan looks for human-facing English.
        // We use a narrow set so the test is robust to future copy edits.
        string[] englishMarkers = { ">Submitted<", ">Approved<", ">Rejected<", ">Resubmitted<",
                                    ">Please<", ">Click here<", ">Dear<" };
        var root = FindViewsRoot();
        foreach (var name in AllVariantViewNames())
        {
            var html = File.ReadAllText(Path.Combine(root, $"{name}.cshtml"));
            foreach (var marker in englishMarkers)
            {
                Assert.That(html, Does.Not.Contain(marker),
                    $"FR-025: '{name}.cshtml' contains English-only marker '{marker}'.");
            }
        }
    }

    [Test]
    public void Rejection_body_does_not_embed_reviewer_internal_commentary()
    {
        // NFR-003: the rejection variant body must NOT contain raw reviewer
        // commentary or per-item rejection reasons. The body links to the
        // decision-detail surface where authorization is enforced server-side.
        // We assert by negative-match: there is no model.ReviewerComment-style
        // token in the rejection bodies.
        var root = FindViewsRoot();
        foreach (var path in new[]
                 {
                     Path.Combine(root, "ApplicationRejected.cshtml"),
                     Path.Combine(root, "ApplicationRejected.text.cshtml"),
                 })
        {
            var contents = File.ReadAllText(path);
            Assert.That(contents, Does.Not.Contain("ReviewerComment"),
                $"NFR-003 violation: '{Path.GetFileName(path)}' references reviewer commentary verbatim.");
            Assert.That(contents, Does.Not.Contain("RejectionReason"),
                $"NFR-003 violation: '{Path.GetFileName(path)}' references a rejection-reason field verbatim.");
        }
    }

    [Test]
    public void Layout_routes_through_branded_header_and_partner_footer()
    {
        // Spec 041 — the shell composes the hosted-logo header + partner-strip
        // footer (replacing the spec-021 text-only wordmark) and carries the
        // "Equipo Programa Semilla" sign-off via EmailBrand.
        var root = FindViewsRoot();
        var layout = File.ReadAllText(Path.Combine(root, "_EmailLayout.cshtml"));
        Assert.That(layout, Does.Contain("_BrandHeader"),
            "Layout must compose the branded logo header partial.");
        Assert.That(layout, Does.Contain("_PartnerFooter"),
            "Layout must compose the partner-strip footer partial.");
        Assert.That(layout, Does.Contain("EmailBrand.SignOff"),
            "Layout must render the centralized sign-off (Equipo Programa Semilla).");
        Assert.That(layout, Does.Contain("#008a9e"),
            "Layout must use the brand teal palette (FR-003).");
    }

    [Test]
    public void Partner_footer_links_support_email_via_brand_constant()
    {
        // FR-006 — the footer support mailto + link text both come from the
        // EmailBrand.SupportEmail constant (single source of truth), which resolves
        // to soporte@programa-semilla.cr.
        var root = FindViewsRoot();
        var footer = File.ReadAllText(Path.Combine(root, "Shared", "_PartnerFooter.cshtml"));
        Assert.That(footer, Does.Contain("mailto:@EmailBrand.SupportEmail"),
            "Footer mailto must reference the EmailBrand.SupportEmail constant.");
        Assert.That(FundingPlatform.Web.Services.Emails.EmailBrand.SupportEmail,
            Is.EqualTo("soporte@programa-semilla.cr"));
    }
}
