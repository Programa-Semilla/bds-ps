using FundingPlatform.Application.Notifications.Templates;
using FundingPlatform.Domain.Notifications;
using FundingPlatform.Tests.E2E.Fixtures;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 021 / T081 / FR-032 / SC-005 — replaces the spec-019 placeholder
/// <c>Assert.Ignore</c> with real per-event-variant assertions. One
/// <c>[Test]</c> per <see cref="NotificationEvent"/> value:
///
/// <list type="bullet">
///   <item>Sender display reads <c>Programa Semilla / Sistema de Banca para el Desarrollo</c>.</item>
///   <item>Signature block present.</item>
///   <item>No inline <c>&lt;img&gt;</c> tag.</item>
///   <item>No <c>Capital Semilla</c> / <c>Forge</c> leakage.</item>
///   <item>Subject template renders correctly under the 78-char cap.</item>
/// </list>
///
/// <para>
/// Assertions run against the source <c>.cshtml</c> files plus the binding
/// catalog — the live render-against-MailCapture path is exercised by the
/// US1–US7 E2E suite when T086 runs. This test preserves the namespace and
/// class name of the original spec-019 placeholder so its test-explorer
/// reference does not break.
/// </para>
/// </summary>
public class EmailTemplateSenderTests : AuthenticatedTestBase
{
    private static readonly string ViewsRoot = FindViewsRoot();

    private static string FindViewsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FundingPlatform.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Could not find solution root.");
        return Path.Combine(dir.FullName, "src/FundingPlatform.Web/Views/Emails");
    }

    private static IEnumerable<TestCaseData> Variants()
    {
        foreach (NotificationEvent ev in Enum.GetValues(typeof(NotificationEvent)))
        {
            yield return new TestCaseData(ev).SetName($"Email_variant_{ev}_satisfies_brand_invariants");
        }
    }

    [TestCaseSource(nameof(Variants))]
    public void Email_variant_satisfies_brand_invariants(NotificationEvent ev)
    {
        var binding = NotificationTemplateBindings.For(ev);
        var html = File.ReadAllText(Path.Combine(ViewsRoot, $"{binding.HtmlViewName}.cshtml"));
        var text = File.ReadAllText(Path.Combine(ViewsRoot, $"{binding.TextViewName}.cshtml"));
        var layout = File.ReadAllText(Path.Combine(ViewsRoot, "_EmailLayout.cshtml"));
        var footer = File.ReadAllText(Path.Combine(ViewsRoot, "_SupportFooter.cshtml"));

        // Strip Razor comments so policy notes don't trip the brand-grep gate.
        string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(
            s, @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var allSources = string.Join("\n",
            Strip(html), Strip(text), Strip(layout), Strip(footer));

        // FR-014 / spec 019 sender display.
        Assert.That(layout, Does.Contain("Programa Semilla"),
            $"FR-014: Sender display 'Programa Semilla' missing in layout for {ev}.");
        Assert.That(layout, Does.Contain("Sistema de Banca para el Desarrollo"),
            $"FR-014: Sender sub-line 'Sistema de Banca para el Desarrollo' missing in layout for {ev}.");

        // Signature block is in the layout.
        Assert.That(layout, Does.Contain("Saludos cordiales"),
            $"Signature block missing in layout for {ev}.");

        // NFR-001 — no inline <img>.
        Assert.That(Strip(html), Does.Not.Contain("<img"),
            $"NFR-001: inline <img> in HTML body for {ev}.");
        Assert.That(Strip(layout), Does.Not.Contain("<img"),
            $"NFR-001: inline <img> in layout (affects {ev}).");

        // FR-027 / SC-006 — no Capital Semilla / Forge.
        Assert.That(allSources, Does.Not.Contain("Capital Semilla"),
            $"FR-027: 'Capital Semilla' leakage affecting {ev}.");
        Assert.That(allSources, Does.Not.Contain("Forge"),
            $"FR-027: 'Forge' leakage affecting {ev}.");

        // Subject template renders within the 78-char cap.
        var rendered = NotificationTemplateBindings.RenderSubject(
            ev, applicantName: "Pedro Pérez", applicationId: 42);
        Assert.That(rendered.Length, Is.LessThanOrEqualTo(NotificationTemplateBindings.MaxSubjectLength),
            $"Subject template for {ev} exceeds 78-char cap.");
        Assert.That(rendered, Is.Not.Null.And.Not.Empty);
    }
}
