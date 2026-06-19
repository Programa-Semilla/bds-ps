using FundingPlatform.Application.Notifications.Templates;
using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 041 / T037 — per-<see cref="NotificationEvent"/> source-level brand
/// invariants for the redesigned outbox emails. Reads the <c>.cshtml</c> files +
/// the shared shell/partials (no Razor engine / no Aspire boot needed). The live
/// rendered-output sweep (logo, partner strip, teal CTA, ALIA naming) is asserted
/// by the mail-capture E2E in ApplicationSubmittedNotificationsTests (T013).
///
/// <para>Supersedes the spec-021 placeholder of the same name: spec 041 reverses
/// the old text-only / no-image rule, so the assertions now require the branded
/// header + partner footer and forbid the retired near-black palette.</para>
/// </summary>
[TestFixture]
public class EmailTemplateSenderTests
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

    private static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(
        s, @"@\*.*?\*@", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

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

        var bodySources = string.Join("\n", Strip(html), Strip(text));

        // Spec 041 — the shared shell composes the branded header + partner footer
        // and carries the centralized "Equipo Programa Semilla" sign-off + teal.
        Assert.That(layout, Does.Contain("_BrandHeader"),
            $"Layout must compose the branded logo header (affects {ev}).");
        Assert.That(layout, Does.Contain("_PartnerFooter"),
            $"Layout must compose the partner-strip footer (affects {ev}).");
        Assert.That(layout, Does.Contain("EmailBrand.SignOff"),
            $"Layout must render the centralized sign-off (affects {ev}).");

        // FR-003 — retired near-black palette must not reappear in any body/text.
        Assert.That(bodySources.ToLowerInvariant(), Does.Not.Contain("#1d1d1f"),
            $"FR-003: retired near-black #1d1d1f present for {ev}.");

        // NFR-004 — imagery lives in the shared chrome; bodies carry no content <img>.
        Assert.That(Strip(html), Does.Not.Contain("<img"),
            $"NFR-004: content <img> in HTML body for {ev} (imagery belongs in shared partials).");

        // SC-006 — no legacy brand leakage.
        Assert.That(bodySources, Does.Not.Contain("Capital Semilla"),
            $"SC-006: 'Capital Semilla' leakage affecting {ev}.");
        Assert.That(bodySources, Does.Not.Contain("Forge"),
            $"SC-006: 'Forge' leakage affecting {ev}.");

        // Subject template renders within the 78-char cap.
        var rendered = NotificationTemplateBindings.RenderSubject(
            ev, applicantName: "Pedro Pérez", applicationId: 42);
        Assert.That(rendered.Length, Is.LessThanOrEqualTo(NotificationTemplateBindings.MaxSubjectLength),
            $"Subject template for {ev} exceeds 78-char cap.");
        Assert.That(rendered, Is.Not.Null.And.Not.Empty);
    }
}
