// Spec 021 / FR-028 + Spec 041 / T017 — password-reset email envelope.
// Spec 041 routes this through the shared branded _EmailLayout via IEmailViewRenderer
// (Decision 1) instead of plain-text token substitution.

using System.Globalization;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Notifications.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 021 / FR-028 + Spec 041 / T017 — composes the es-CR password-reset email,
/// rendered through the shared branded <c>_EmailLayout</c>
/// (<c>Views/Emails/Identity/ForgotPasswordEmail.cshtml</c> + <c>.text.cshtml</c>)
/// via <see cref="IEmailViewRenderer"/>. Direct-send (not the spec-021 outbox).
/// </summary>
public sealed class ForgotPasswordEmailFactory
{
    private const string HtmlView = "~/Views/Emails/Identity/ForgotPasswordEmail.cshtml";
    private const string TextView = "~/Views/Emails/Identity/ForgotPasswordEmail.text.cshtml";
    // FR-028 / contracts/public-routes.md — subject line is fixed.
    private const string Subject = "Restablezca su contraseña";

    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IConfiguration _config;
    private readonly ILogger<ForgotPasswordEmailFactory> _logger;

    public ForgotPasswordEmailFactory(
        IEmailViewRenderer viewRenderer,
        IConfiguration config,
        ILogger<ForgotPasswordEmailFactory> logger)
    {
        _viewRenderer = viewRenderer;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Builds the password-reset envelope. <paramref name="resetLink"/> is the
    /// absolute reset URL (CTA); <paramref name="expiresAt"/> is formatted in es-CR
    /// local time. Renders the branded HTML + plain-text twin (FR-009).
    /// </summary>
    public async Task<EmailMessage> BuildAsync(
        string toAddress,
        string? applicantFirstName,
        string resetLink,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(resetLink);

        var expiresAtLocal = expiresAt
            .ToOffset(TimeSpan.FromHours(-6)) // CR is UTC-6 (no DST)
            .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));

        var baseUrl = _config["Notifications:BaseUrl"];
        var model = new DirectEmailModel(
            Subject: Subject,
            HeroTitle: "Restablecé tu contraseña",
            DisplayName: applicantFirstName ?? string.Empty,
            Paragraphs: new[]
            {
                "Recibimos una solicitud para restablecer la contraseña de tu cuenta en ALIA.",
                "Hacé clic en el botón para escoger una nueva contraseña. Si vos no solicitaste este cambio, podés ignorar este mensaje.",
            },
            CtaUrl: resetLink,
            CtaLabel: "Restablecer contraseña",
            CardHeading: null,
            CardRows: null,
            FooterNote: $"El enlace es de un solo uso y expira el {expiresAtLocal}.",
            LogoUrl: BrandAssets.LogoUrl(baseUrl),
            PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

        var html = await _viewRenderer.RenderViewAsync(HtmlView, model, disableLayout: false, ct);
        var text = await _viewRenderer.RenderViewAsync(TextView, model, disableLayout: true, ct);
        _logger.LogDebug("Built branded forgot-password email for {To}.", toAddress);
        return new EmailMessage(toAddress, Subject, html, text);
    }
}
