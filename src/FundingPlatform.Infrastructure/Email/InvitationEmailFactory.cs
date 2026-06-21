// Spec 033 / FR-001 + Spec 041 / T017 — set-password invitation email.
// Spec 041 routes this through the shared branded _EmailLayout via IEmailViewRenderer
// (Decision 1, reference copy #1 "Bienvenida a ALIA") instead of token substitution.

using System.Globalization;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Notifications.Email;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 033 / FR-001 + Spec 041 / T017 — composes the es-CR set-password invitation
/// email, rendered through the shared branded <c>_EmailLayout</c>
/// (<c>Views/Emails/Identity/InvitationEmail.cshtml</c> + <c>.text.cshtml</c>) via
/// <see cref="IEmailViewRenderer"/>. Direct-send (D5), not the spec-021 outbox.
/// </summary>
public sealed class InvitationEmailFactory
{
    private const string HtmlView = "~/Views/Emails/Identity/InvitationEmail.cshtml";
    private const string TextView = "~/Views/Emails/Identity/InvitationEmail.text.cshtml";
    // Spec 033 / C4 — fixed es-CR subject.
    private const string Subject = "Le han creado una cuenta — establezca su contraseña";

    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IEmailBaseUrlProvider _baseUrlProvider;
    private readonly ILogger<InvitationEmailFactory> _logger;

    public InvitationEmailFactory(
        IEmailViewRenderer viewRenderer,
        IEmailBaseUrlProvider baseUrlProvider,
        ILogger<InvitationEmailFactory> logger)
    {
        _viewRenderer = viewRenderer;
        _baseUrlProvider = baseUrlProvider;
        _logger = logger;
    }

    /// <summary>
    /// Builds the invitation envelope. <paramref name="inviteLink"/> is the absolute
    /// <c>/Account/ResetPassword</c> URL (CTA); <paramref name="expiresAt"/> is es-CR
    /// local time. Razor auto-encodes the recipient name in both bodies (XSS-safe).
    /// </summary>
    public async Task<EmailMessage> BuildAsync(
        string toAddress,
        string? firstName,
        string inviteLink,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteLink);

        var expiresAtLocal = expiresAt
            .ToOffset(TimeSpan.FromHours(-6)) // CR is UTC-6 (no DST)
            .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));

        var baseUrl = _baseUrlProvider.GetBaseUrl();
        var model = new DirectEmailModel(
            Subject: Subject,
            HeroTitle: "Bienvenida a ALIA",
            DisplayName: firstName ?? string.Empty,
            Paragraphs: new[]
            {
                "Te damos la bienvenida a ALIA, la plataforma digital para solicitar, gestionar y dar seguimiento a fondos de capital semilla.",
                "Tu cuenta fue creada correctamente. Para comenzar, establecé tu contraseña con el siguiente botón.",
                "Te recomendamos revisar tus datos de acceso en el primer ingreso, en caso de que corresponda.",
            },
            CtaUrl: inviteLink,
            CtaLabel: "Establecer mi contraseña",
            CardHeading: null,
            CardRows: null,
            FooterNote: $"El enlace es de un solo uso y expira el {expiresAtLocal}.",
            LogoUrl: BrandAssets.LogoUrl(baseUrl),
            PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

        var html = await _viewRenderer.RenderViewAsync(HtmlView, model, disableLayout: false, ct);
        var text = await _viewRenderer.RenderViewAsync(TextView, model, disableLayout: true, ct);
        _logger.LogDebug("Built branded invitation email for {To}.", toAddress);
        return new EmailMessage(toAddress, Subject, html, text);
    }
}
