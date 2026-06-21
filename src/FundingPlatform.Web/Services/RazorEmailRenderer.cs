using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Application.Notifications.Templates;
using FundingPlatform.Domain.Notifications;

namespace FundingPlatform.Web.Services;

/// <summary>
/// Spec 021 / T023 / FR-023 — Razor-backed implementation of
/// <see cref="IEmailTemplateRenderer"/>. Renders every variant's HTML body
/// (<c>Views/Emails/{ViewName}.cshtml</c>) AND plain-text fallback
/// (<c>Views/Emails/{ViewName}.text.cshtml</c>) under the shared
/// <c>_EmailLayout.cshtml</c> layout. Throws <see cref="EmailRenderException"/>
/// on render failure so the worker can map to PermanentFailure (FR-022).
///
/// <para>
/// The renderer is BackgroundService-safe: it constructs a fresh
/// <see cref="DefaultHttpContext"/> per call (no ambient HTTP request needed).
/// Mirrors the off-thread pattern in <see cref="RazorFundingAgreementHtmlRenderer"/>.
/// </para>
/// </summary>
public sealed class RazorEmailRenderer : IEmailTemplateRenderer
{
    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IEmailBaseUrlProvider _baseUrlProvider;

    public RazorEmailRenderer(
        IEmailViewRenderer viewRenderer,
        IEmailBaseUrlProvider baseUrlProvider)
    {
        _viewRenderer = viewRenderer;
        _baseUrlProvider = baseUrlProvider;
    }

    public async Task<RenderedEmail> RenderAsync(
        NotificationEvent eventType,
        NotificationRecipient recipient,
        NotificationPayload payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(payload);

        var binding = NotificationTemplateBindings.For(eventType);
        var subject = NotificationTemplateBindings.RenderSubject(
            eventType, payload.ApplicantDisplayName, payload.ApplicationId);

        // FR-026 / spec 028 R-001 — composed deep link from the resolved base URL
        // + the event's CtaRouteTemplate (event-driven, not bucket-derived). The
        // dispatch worker has no request context, so the provider falls back to
        // Notifications:BaseUrl here.
        var baseUrl = _baseUrlProvider.GetBaseUrl();
        var ctaUrl = ComposeCtaUrl(eventType, baseUrl, payload.ApplicationId);

        // Spec 041 / Decision 2 / FR-002 — absolute brand-image URLs composed from
        // the same resolved base URL against the official assets in wwwroot/lib/brand.
        var logoUrl = Combine(baseUrl, BrandAssets.LogoPath);
        var partnerStripUrl = Combine(baseUrl, BrandAssets.PartnerStripPath);

        var model = new EmailRenderModel(
            EventType: eventType,
            Recipient: recipient,
            Payload: payload,
            Subject: subject,
            CtaUrl: ctaUrl,
            LogoUrl: logoUrl,
            PartnerStripUrl: partnerStripUrl);

        string htmlBody;
        string textBody;
        try
        {
            htmlBody = await _viewRenderer.RenderViewAsync(
                $"~/Views/Emails/{binding.HtmlViewName}.cshtml", model, disableLayout: false, ct);
            textBody = await _viewRenderer.RenderViewAsync(
                $"~/Views/Emails/{binding.TextViewName}.cshtml", model, disableLayout: true, ct);
        }
        catch (Exception ex) when (ex is not EmailRenderException)
        {
            throw new EmailRenderException(
                $"Render failed for {eventType.ToStorageString()} ({binding.HtmlViewName}/{binding.TextViewName}): {ex.Message}",
                ex);
        }

        return new RenderedEmail(subject, htmlBody, textBody);
    }

    /// <summary>
    /// Spec 028 / R-001 / FR-026 — composes the CTA deep link from the event's
    /// <see cref="NotificationTemplateBindings.Binding.CtaRouteTemplate"/>. The
    /// literal <c>{id}</c> token is replaced with the ApplicationId; templates
    /// with no token (e.g. <c>/Review/SigningInbox</c>, the soft-deleted-withdrawal
    /// <c>/Review</c>) are used verbatim. The CTA destination is now a function of
    /// the event, NOT the recipient bucket (it replaced the spec-021 bucket branch).
    /// </summary>
    public static string ComposeCtaUrl(
        NotificationEvent eventType, string baseUrl, int applicationId)
    {
        var template = NotificationTemplateBindings.For(eventType).CtaRouteTemplate;
        var path = template.Replace(
            "{id}",
            applicationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        return Combine(baseUrl, path);
    }

    private static string Combine(string baseUrl, string path)
    {
        if (string.IsNullOrEmpty(baseUrl)) return path;
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }
}

/// <summary>
/// Spec 021 / T024 — model exposed to every email Razor view. Carries
/// everything the layout, body, and support footer need to render without
/// re-reading config or re-doing string composition inside the view.
/// </summary>
public sealed record EmailRenderModel(
    NotificationEvent EventType,
    NotificationRecipient Recipient,
    NotificationPayload Payload,
    string Subject,
    string CtaUrl,
    // Spec 041 / Decision 2 / T004 — absolute brand-image URLs so views/partials
    // never hard-code a host. Composed from Notifications:BaseUrl in RenderAsync.
    // (The From: sender display lives in config + the sender impls, not here.)
    string LogoUrl,
    string PartnerStripUrl) : IBrandedEmailModel;
