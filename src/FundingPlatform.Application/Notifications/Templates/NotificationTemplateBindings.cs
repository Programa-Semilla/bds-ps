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

            // =================================================================
            // Spec 028 — post-resolution notification events (12). Subjects,
            // view names, and CTA routes are the source of truth in
            // contracts/notification-events.md. es-CR; one HTML + text partial
            // pair each under Views/Emails/.
            // =================================================================

            // US1 — applicant responded to the resolution → reviewers + admins.
            [NotificationEvent.ResponseSubmittedReviewer] = new(
                NotificationEvent.ResponseSubmittedReviewer,
                SubjectTemplate: "El solicitante respondió la resolución — Solicitud #{ApplicationId}",
                HtmlViewName:    "ResponseSubmittedReviewer",
                TextViewName:    "ResponseSubmittedReviewer.text",
                TemplateVariantKey: "reviewer-response-submitted",
                CtaRouteTemplate: "/Review/{id}"),

            // US2 — appeal opened → reviewers + admins.
            [NotificationEvent.AppealOpenedReviewer] = new(
                NotificationEvent.AppealOpenedReviewer,
                SubjectTemplate: "Nueva apelación abierta — Solicitud #{ApplicationId}",
                HtmlViewName:    "AppealOpenedReviewer",
                TextViewName:    "AppealOpenedReviewer.text",
                TemplateVariantKey: "reviewer-appeal-opened",
                CtaRouteTemplate: "/ApplicantResponse/Appeal/{id}"),

            // US2 — applicant posted an appeal message → reviewers + admins.
            [NotificationEvent.AppealMessageReviewer] = new(
                NotificationEvent.AppealMessageReviewer,
                SubjectTemplate: "Nuevo mensaje en la apelación — Solicitud #{ApplicationId}",
                HtmlViewName:    "AppealMessageReviewer",
                TextViewName:    "AppealMessageReviewer.text",
                TemplateVariantKey: "reviewer-appeal-message",
                CtaRouteTemplate: "/ApplicantResponse/Appeal/{id}"),

            // US2 — reviewer posted an appeal message → applicant + admins.
            [NotificationEvent.AppealMessageApplicant] = new(
                NotificationEvent.AppealMessageApplicant,
                SubjectTemplate: "Nuevo mensaje del revisor en tu apelación — Solicitud #{ApplicationId}",
                HtmlViewName:    "AppealMessageApplicant",
                TextViewName:    "AppealMessageApplicant.text",
                TemplateVariantKey: "applicant-appeal-message",
                CtaRouteTemplate: "/ApplicantResponse/Index/{id}"),

            // US2 — appeal resolved → applicant + admins. Body switches on OutcomeCode.
            [NotificationEvent.AppealResolvedApplicant] = new(
                NotificationEvent.AppealResolvedApplicant,
                SubjectTemplate: "Resolución de tu apelación — Solicitud #{ApplicationId}",
                HtmlViewName:    "AppealResolvedApplicant",
                TextViewName:    "AppealResolvedApplicant.text",
                TemplateVariantKey: "applicant-appeal-resolved",
                CtaRouteTemplate: "/ApplicantResponse/Index/{id}"),

            // US2 — appeal granted as reopen-to-review (dual-fire) → reviewers + admins.
            [NotificationEvent.AppealReopenedReviewer] = new(
                NotificationEvent.AppealReopenedReviewer,
                SubjectTemplate: "Apelación concedida: solicitud reabierta para revisión — Solicitud #{ApplicationId}",
                HtmlViewName:    "AppealReopenedReviewer",
                TextViewName:    "AppealReopenedReviewer.text",
                TemplateVariantKey: "reviewer-appeal-reopened",
                CtaRouteTemplate: "/Review/{id}"),

            // US3 — convenio generated/regenerated → applicant + admins.
            [NotificationEvent.AgreementGeneratedApplicant] = new(
                NotificationEvent.AgreementGeneratedApplicant,
                SubjectTemplate: "Tu convenio está listo para firmar — Solicitud #{ApplicationId}",
                HtmlViewName:    "AgreementGeneratedApplicant",
                TextViewName:    "AgreementGeneratedApplicant.text",
                TemplateVariantKey: "applicant-agreement-generated",
                CtaRouteTemplate: "/Applications/{id}/FundingAgreement"),

            // US3 — applicant uploaded a signed convenio → reviewers + admins.
            [NotificationEvent.SignedUploadSubmittedReviewer] = new(
                NotificationEvent.SignedUploadSubmittedReviewer,
                SubjectTemplate: "Convenio firmado recibido para revisión — Solicitud #{ApplicationId}",
                HtmlViewName:    "SignedUploadSubmittedReviewer",
                TextViewName:    "SignedUploadSubmittedReviewer.text",
                TemplateVariantKey: "reviewer-signed-upload-submitted",
                CtaRouteTemplate: "/Review/SigningInbox"),

            // US3 — applicant replaced the pending signed upload → reviewers + admins.
            [NotificationEvent.SignedUploadReplacedReviewer] = new(
                NotificationEvent.SignedUploadReplacedReviewer,
                SubjectTemplate: "Convenio firmado reemplazado — Solicitud #{ApplicationId}",
                HtmlViewName:    "SignedUploadReplacedReviewer",
                TextViewName:    "SignedUploadReplacedReviewer.text",
                TemplateVariantKey: "reviewer-signed-upload-replaced",
                CtaRouteTemplate: "/Review/SigningInbox"),

            // US3 — applicant withdrew the pending signed upload → reviewers + admins.
            [NotificationEvent.SignedUploadWithdrawnReviewer] = new(
                NotificationEvent.SignedUploadWithdrawnReviewer,
                SubjectTemplate: "Convenio firmado retirado — Solicitud #{ApplicationId}",
                HtmlViewName:    "SignedUploadWithdrawnReviewer",
                TextViewName:    "SignedUploadWithdrawnReviewer.text",
                TemplateVariantKey: "reviewer-signed-upload-withdrawn",
                CtaRouteTemplate: "/Review/SigningInbox"),

            // US3 — reviewer approved the signed convenio (executed) → applicant + admins.
            [NotificationEvent.AgreementExecutedApplicant] = new(
                NotificationEvent.AgreementExecutedApplicant,
                SubjectTemplate: "Tu convenio fue ejecutado — Solicitud #{ApplicationId}",
                HtmlViewName:    "AgreementExecutedApplicant",
                TextViewName:    "AgreementExecutedApplicant.text",
                TemplateVariantKey: "applicant-agreement-executed",
                CtaRouteTemplate: "/Applications/{id}/FundingAgreement"),

            // US3 — reviewer rejected the signed convenio (changes required) → applicant + admins.
            [NotificationEvent.SignedUploadRejectedApplicant] = new(
                NotificationEvent.SignedUploadRejectedApplicant,
                SubjectTemplate: "Tu convenio firmado requiere cambios — Solicitud #{ApplicationId}",
                HtmlViewName:    "SignedUploadRejectedApplicant",
                TextViewName:    "SignedUploadRejectedApplicant.text",
                TemplateVariantKey: "applicant-signed-upload-rejected",
                CtaRouteTemplate: "/Applications/{id}/FundingAgreement"),

            // Spec 041 / US2 / FR-011 — applicant learns a reviewer began review.
            // Fired at the Submitted → UnderReview transition; applicant-only.
            [NotificationEvent.ApplicationUnderReviewApplicant] = new(
                NotificationEvent.ApplicationUnderReviewApplicant,
                SubjectTemplate: "Tu solicitud está en revisión — Solicitud #{ApplicationId}",
                HtmlViewName:    "ApplicationUnderReviewApplicant",
                TextViewName:    "ApplicationUnderReviewApplicant.text",
                TemplateVariantKey: "applicant-under-review",
                CtaRouteTemplate: "/Application/Details/{id}"),
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
