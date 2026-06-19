// Spec 021 / FR-025 + Spec 041 / T018 — stage-expiry reminder emails.
// Spec 041 routes the three reminder variants through the shared branded
// _EmailLayout via IEmailViewRenderer (Decision 1) instead of token substitution.

using System.Globalization;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 021 / FR-025 + Spec 041 / T018 — composes the three reminder envelopes
/// (T-72h, T-24h, expiry) as branded emails rendered through the shared
/// <c>_EmailLayout</c> (<c>Views/Emails/Stages/*</c> + <c>.text.cshtml</c> twins)
/// via <see cref="IEmailViewRenderer"/>. The hosted reminder service runs outside
/// the HTTP request scope; the renderer is BackgroundService-safe.
/// </summary>
public sealed class StageReminderEmailFactory
{
    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IConfiguration _config;
    private readonly ILogger<StageReminderEmailFactory> _logger;

    public StageReminderEmailFactory(
        IEmailViewRenderer viewRenderer,
        IConfiguration config,
        ILogger<StageReminderEmailFactory> logger)
    {
        _viewRenderer = viewRenderer;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Builds the branded reminder envelope for <paramref name="bucket"/>.
    /// </summary>
    public async Task<EmailMessage> BuildAsync(
        ReminderBucket bucket,
        string toAddress,
        string applicantFirstName,
        string publicCode,
        StageKind stage,
        DateTimeOffset closesAt,
        CancellationToken ct = default)
    {
        if (bucket is ReminderBucket.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bucket), bucket, "ReminderBucket.None has no associated email.");
        }

        var stageLabel = StageLabel(stage);
        var closesAtLocal = closesAt
            .ToOffset(TimeSpan.FromHours(-6)) // CR is UTC-6 (no DST)
            .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));

        var (viewName, subject, heroTitle, paragraphs) = bucket switch
        {
            ReminderBucket.T72h => (
                "T72ReminderEmail",
                $"Su solicitud {publicCode} cierra en 72 horas",
                "Tu solicitud cierra en 72 horas",
                new[]
                {
                    $"Te recordamos que la etapa {stageLabel} de tu solicitud {publicCode} está por cerrar.",
                    "Si la ventana se cumple sin acción, la solicitud quedará bloqueada y cualquier intento de envío será rechazado por el sistema.",
                }),
            ReminderBucket.T24h => (
                "T24ReminderEmail",
                $"Su solicitud {publicCode} cierra en 24 horas",
                "Tu solicitud cierra en 24 horas",
                new[]
                {
                    $"Quedan menos de 24 horas para que la etapa {stageLabel} de tu solicitud {publicCode} cierre.",
                    "Si la ventana se cumple sin acción, la solicitud quedará bloqueada y cualquier intento de envío será rechazado por el sistema.",
                }),
            ReminderBucket.Expired => (
                "ExpiredEmail",
                $"La etapa de {publicCode} cerró el {closesAtLocal}",
                "La etapa de tu solicitud cerró",
                new[]
                {
                    $"Te informamos que la etapa {stageLabel} de tu solicitud {publicCode} cerró.",
                    "La ventana se cumplió; cualquier intento de envío será rechazado por el sistema. Si necesitás ayuda, escribí al equipo de soporte.",
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
        };

        var baseUrl = _config["Notifications:BaseUrl"];
        var model = new DirectEmailModel(
            Subject: subject,
            HeroTitle: heroTitle,
            DisplayName: applicantFirstName ?? string.Empty,
            Paragraphs: paragraphs,
            CtaUrl: null,   // FR-005 — the reminder carries no link variable; no CTA invented.
            CtaLabel: null,
            CardHeading: "Detalle de la solicitud",
            CardRows: new[]
            {
                new DetailRow("Código", publicCode),
                new DetailRow("Etapa", stageLabel),
                new DetailRow("Cierre programado", closesAtLocal),
            },
            FooterNote: null,
            LogoUrl: BrandAssets.LogoUrl(baseUrl),
            PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

        var html = await _viewRenderer.RenderViewAsync(
            $"~/Views/Emails/Stages/{viewName}.cshtml", model, disableLayout: false, ct);
        var text = await _viewRenderer.RenderViewAsync(
            $"~/Views/Emails/Stages/{viewName}.text.cshtml", model, disableLayout: true, ct);
        _logger.LogDebug("Built branded stage-reminder email ({Bucket}) for {To}.", bucket, toAddress);
        return new EmailMessage(toAddress, subject, html, text);
    }

    private static string StageLabel(StageKind stage) => stage switch
    {
        StageKind.Solicitud => "Solicitud",
        StageKind.Revision => "Revisión",
        StageKind.Facturacion => "Facturación",
        _ => stage.ToString(),
    };
}
