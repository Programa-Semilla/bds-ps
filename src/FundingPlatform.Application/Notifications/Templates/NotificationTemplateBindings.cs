using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Application.Notifications.Templates;

/// <summary>
/// Spec 021 / T016 / FR-024 / Event Catalog v1 — closed map from
/// <see cref="NotificationEvent"/> to (subject template, HTML view name,
/// text view name, template variant key).
///
/// <para>
/// Subjects use es-CR copy with <c>{ApplicantName}</c> + <c>{ApplicationId}</c>
/// placeholders. Subject interpolation lives in the renderer; this map is
/// data-only so unit tests can assert every enum value has a binding.
/// </para>
/// <para>
/// Per FR-024 the participating-admin bucket reuses the same body partial
/// the bucket-priority-winning variant uses. There are six <c>.cshtml</c>
/// HTML files + six <c>.text.cshtml</c> text fallbacks — twelve total.
/// </para>
/// <para>
/// Spec 028 / R-001 — the CTA destination is now event-driven via
/// <see cref="Binding.CtaRouteTemplate"/> rather than bucket-derived. Each
/// event names exactly one route template (e.g. <c>/Review/{id}</c>,
/// <c>/Review/SigningInbox</c>, <c>/ApplicantResponse/Index/{id}</c>); the
/// renderer substitutes <c>{id}</c> with the ApplicationId and composes the
/// absolute deep link from <c>Notifications:BaseUrl</c>. The existing spec-021
/// events keep their original routes (the route of the bucket the event is
/// named for), so observed CTAs are unchanged for them.
/// </para>
/// </summary>
public static class NotificationTemplateBindings
{
    /// <summary>One row per enum value, exposed for unit-test coverage.</summary>
    /// <param name="CtaRouteTemplate">
    /// Spec 028 / R-001 — relative route the email CTA points at. A literal
    /// <c>{id}</c> token is replaced with the ApplicationId by the renderer;
    /// templates with no token (e.g. <c>/Review/SigningInbox</c>) are used verbatim.
    /// </param>
    public sealed record Binding(
        NotificationEvent Event,
        string SubjectTemplate,
        string HtmlViewName,
        string TextViewName,
        string TemplateVariantKey,
        string CtaRouteTemplate);

    /// <summary>Closed map. Adding a new event MUST add a row here AND a view file.</summary>
    public static readonly IReadOnlyDictionary<NotificationEvent, Binding> Bindings =
        new Dictionary<NotificationEvent, Binding>
        {
            [NotificationEvent.ApplicationSubmittedReviewer] = new(
                NotificationEvent.ApplicationSubmittedReviewer,
                // {ApplicantName} → applicant first + last (PayloadJson.ApplicantDisplayName).
                SubjectTemplate: "Nueva solicitud para revisar: {ApplicantName}",
                HtmlViewName:    "ApplicationSubmittedReviewer",
                TextViewName:    "ApplicationSubmittedReviewer.text",
                TemplateVariantKey: "reviewer-application-submitted",
                CtaRouteTemplate: "/Review/{id}"),

            [NotificationEvent.ApplicationSubmittedApplicant] = new(
                NotificationEvent.ApplicationSubmittedApplicant,
                // {ApplicationId} → Application.Id (Application.Folio does not exist — R-001 / OQ-007).
                SubjectTemplate: "Recibimos tu solicitud — Solicitud #{ApplicationId}",
                HtmlViewName:    "ApplicationSubmittedApplicant",
                TextViewName:    "ApplicationSubmittedApplicant.text",
                TemplateVariantKey: "applicant-application-submitted",
                CtaRouteTemplate: "/Application/Details/{id}"),

            [NotificationEvent.ReturnedToApplicant] = new(
                NotificationEvent.ReturnedToApplicant,
                SubjectTemplate: "Acción requerida: actualiza tu solicitud — Solicitud #{ApplicationId}",
                HtmlViewName:    "ReturnedToApplicant",
                TextViewName:    "ReturnedToApplicant.text",
                TemplateVariantKey: "applicant-returned",
                CtaRouteTemplate: "/Application/Details/{id}"),

            [NotificationEvent.ResubmittedByApplicant] = new(
                NotificationEvent.ResubmittedByApplicant,
                SubjectTemplate: "Solicitud reenviada para revisión: {ApplicantName}",
                HtmlViewName:    "ResubmittedByApplicant",
                TextViewName:    "ResubmittedByApplicant.text",
                TemplateVariantKey: "reviewer-resubmitted",
                CtaRouteTemplate: "/Review/{id}"),

            [NotificationEvent.ApplicationApproved] = new(
                NotificationEvent.ApplicationApproved,
                SubjectTemplate: "Tu solicitud fue aprobada — Solicitud #{ApplicationId}",
                HtmlViewName:    "ApplicationApproved",
                TextViewName:    "ApplicationApproved.text",
                TemplateVariantKey: "applicant-approved",
                CtaRouteTemplate: "/Application/Details/{id}"),

            [NotificationEvent.ApplicationRejected] = new(
                NotificationEvent.ApplicationRejected,
                SubjectTemplate: "Decisión sobre tu solicitud — Solicitud #{ApplicationId}",
                HtmlViewName:    "ApplicationRejected",
                TextViewName:    "ApplicationRejected.text",
                TemplateVariantKey: "applicant-rejected",
                CtaRouteTemplate: "/Application/Details/{id}"),

            // Spec 021 / US9 / FR-040 — reviewer-bucket notification when an
            // applicant withdraws an UnderReview Application. CTA links to the
            // reviewer queue (/Review), NOT /Review/{id}: the Application is
            // soft-deleted, so the detail route would 403/404.
            [NotificationEvent.WithdrawnByApplicant] = new(
                NotificationEvent.WithdrawnByApplicant,
                SubjectTemplate: "Solicitud retirada por el solicitante: {ApplicantName}",
                HtmlViewName:    "ApplicationWithdrawnByApplicant",
                TextViewName:    "ApplicationWithdrawnByApplicant.text",
                TemplateVariantKey: "reviewer-application-withdrawn",
                // Soft-deleted application: link to the reviewer queue, not /Review/{id}.
                CtaRouteTemplate: "/Review"),
        };

    /// <summary>Lookup helper. Throws on unknown event (closed map invariant).</summary>
    public static Binding For(NotificationEvent ev) =>
        Bindings.TryGetValue(ev, out var binding)
            ? binding
            : throw new ArgumentOutOfRangeException(nameof(ev), ev,
                "No template binding registered for this NotificationEvent");

    /// <summary>
    /// Spec 021 / FR-014 / EC-014 — subject lines may be truncated to 78 chars
    /// (RFC 5322 line length recommendation). Long applicant names get an
    /// ellipsis; the full name still appears in the body.
    /// </summary>
    public const int MaxSubjectLength = 78;

    /// <summary>
    /// Interpolates <c>{ApplicantName}</c> + <c>{ApplicationId}</c> tokens, then
    /// applies the 78-char cap with ellipsis if needed.
    /// </summary>
    public static string RenderSubject(NotificationEvent ev, string applicantName, int applicationId)
    {
        var raw = For(ev).SubjectTemplate
            .Replace("{ApplicantName}", applicantName, StringComparison.Ordinal)
            .Replace("{ApplicationId}", applicationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        return raw.Length <= MaxSubjectLength
            ? raw
            : string.Concat(raw.AsSpan(0, MaxSubjectLength - 1), "…");
    }
}
