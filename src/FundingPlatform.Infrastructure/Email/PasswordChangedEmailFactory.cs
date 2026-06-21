// Spec 041 / US3 / T030 / FR-010/FR-012/FR-014 — password-changed confirmation.
// Direct-send (not the spec-021 outbox); rendered through the shared branded
// _EmailLayout via IEmailViewRenderer. NO CTA (no link variable ⇒ FR-005).

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Notifications.Email;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 041 / US3 — composes the es-CR "Tu contraseña fue actualizada" confirmation
/// (reference copy #2), rendered through the shared branded <c>_EmailLayout</c>
/// (<c>Views/Emails/Identity/PasswordChangedEmail.cshtml</c> + <c>.text.cshtml</c>)
/// via <see cref="IEmailViewRenderer"/>. Mirrors <see cref="ForgotPasswordEmailFactory"/>.
/// Sent best-effort at every password set/change success point; a render/transport
/// failure must never block the password operation (caller catches).
/// </summary>
public sealed class PasswordChangedEmailFactory
{
    private const string HtmlView = "~/Views/Emails/Identity/PasswordChangedEmail.cshtml";
    private const string TextView = "~/Views/Emails/Identity/PasswordChangedEmail.text.cshtml";
    private const string Subject = "Tu contraseña fue actualizada";

    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IEmailBaseUrlProvider _baseUrlProvider;
    private readonly ILogger<PasswordChangedEmailFactory> _logger;

    public PasswordChangedEmailFactory(
        IEmailViewRenderer viewRenderer,
        IEmailBaseUrlProvider baseUrlProvider,
        ILogger<PasswordChangedEmailFactory> logger)
    {
        _viewRenderer = viewRenderer;
        _baseUrlProvider = baseUrlProvider;
        _logger = logger;
    }

    /// <summary>
    /// Builds the password-changed confirmation envelope. No CTA link (security
    /// confirmation only); the recipient is advised to contact support if they did
    /// not make the change — the support phone lives in the shared footer.
    /// </summary>
    public async Task<EmailMessage> BuildAsync(
        string toAddress,
        string? firstName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);

        var baseUrl = _baseUrlProvider.GetBaseUrl();
        var model = new DirectEmailModel(
            Subject: Subject,
            HeroTitle: "Tu contraseña fue actualizada",
            DisplayName: firstName ?? string.Empty,
            Paragraphs: new[]
            {
                "Te confirmamos que la contraseña de tu cuenta en ALIA fue actualizada correctamente.",
                "Si vos realizaste este cambio, no tenés que hacer ninguna acción adicional.",
                "Si no reconocés esta modificación, comunicate de inmediato con el equipo de soporte para revisar la seguridad de tu cuenta.",
            },
            CtaUrl: null,   // FR-005 — security confirmation; no link variable, no CTA.
            CtaLabel: null,
            CardHeading: null,
            CardRows: null,
            FooterNote: null,
            LogoUrl: BrandAssets.LogoUrl(baseUrl),
            PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

        var html = await _viewRenderer.RenderViewAsync(HtmlView, model, disableLayout: false, ct);
        var text = await _viewRenderer.RenderViewAsync(TextView, model, disableLayout: true, ct);
        _logger.LogDebug("Built branded password-changed email for {To}.", toAddress);
        return new EmailMessage(toAddress, Subject, html, text);
    }
}
