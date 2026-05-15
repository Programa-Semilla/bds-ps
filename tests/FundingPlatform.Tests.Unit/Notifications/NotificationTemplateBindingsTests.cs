using FundingPlatform.Application.Notifications.Templates;
using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Tests.Unit.Notifications;

/// <summary>
/// Spec 021 / T040 — assert every <see cref="NotificationEvent"/> enum value
/// has a binding row (subject template, html view name, text view name,
/// variant key). Failure of this test means a new event was added without a
/// view file backing it.
/// </summary>
[TestFixture]
public class NotificationTemplateBindingsTests
{
    [Test]
    public void Every_enum_value_has_a_binding()
    {
        foreach (NotificationEvent ev in Enum.GetValues(typeof(NotificationEvent)))
        {
            Assert.That(NotificationTemplateBindings.Bindings, Does.ContainKey(ev),
                $"NotificationTemplateBindings is missing a binding for {ev}.");
            var binding = NotificationTemplateBindings.For(ev);
            Assert.That(binding.SubjectTemplate, Is.Not.Null.And.Not.Empty,
                $"Binding for {ev} has an empty SubjectTemplate.");
            Assert.That(binding.HtmlViewName, Is.Not.Null.And.Not.Empty);
            Assert.That(binding.TextViewName, Is.Not.Null.And.Not.Empty);
            Assert.That(binding.TemplateVariantKey, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public void Subject_interpolation_replaces_tokens()
    {
        var s = NotificationTemplateBindings.RenderSubject(
            NotificationEvent.ApplicationSubmittedReviewer,
            applicantName: "Juana Pérez",
            applicationId: 42);
        Assert.That(s, Is.EqualTo("Nueva solicitud para revisar: Juana Pérez"));
    }

    [Test]
    public void Subject_interpolation_truncates_at_78_chars_with_ellipsis()
    {
        var veryLongName = new string('A', 200);
        var s = NotificationTemplateBindings.RenderSubject(
            NotificationEvent.ApplicationSubmittedReviewer,
            applicantName: veryLongName,
            applicationId: 1);

        Assert.That(s.Length, Is.LessThanOrEqualTo(NotificationTemplateBindings.MaxSubjectLength));
        Assert.That(s, Does.EndWith("…"));
    }

    [Test]
    public void For_throws_on_unknown_event()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NotificationTemplateBindings.For((NotificationEvent)999));
    }

    [Test]
    public void Storage_string_round_trip()
    {
        foreach (NotificationEvent ev in Enum.GetValues(typeof(NotificationEvent)))
        {
            var s = ev.ToStorageString();
            Assert.That(NotificationEventExtensions.FromStorageString(s), Is.EqualTo(ev));
            Assert.That(s, Does.Match("^[A-Z_]+$"),
                $"Storage string for {ev} must be upper-snake-case (was '{s}').");
        }
    }
}
