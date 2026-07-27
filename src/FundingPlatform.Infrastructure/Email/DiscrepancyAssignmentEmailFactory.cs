// Spec 048 / US4 — discrepancy-assignment notification (branded, direct-send, best-effort).
// Mirrors InvitationEmailFactory: composes a DirectEmailModel and renders it through the shared
// _EmailLayout via IEmailViewRenderer.

using System.Globalization;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Notifications.Email;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 048 / US4 — composes the es-CR "se le asignó una diferencia" email to the responsible
/// operator, rendered through the shared branded <c>_EmailLayout</c>
/// (<c>Views/Emails/DiscrepancyAssignment.cshtml</c> + <c>.text.cshtml</c>). Direct-send (research D6),
/// not the spec-021 outbox; sent best-effort (never blocks the assignment).
/// </summary>
public sealed class DiscrepancyAssignmentEmailFactory
{
    private const string HtmlView = "~/Views/Emails/DiscrepancyAssignment.cshtml";
    private const string TextView = "~/Views/Emails/DiscrepancyAssignment.text.cshtml";
    private const string Subject = "Se le asignó una diferencia de reconciliación";

    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IEmailBaseUrlProvider _baseUrlProvider;
    private readonly ILogger<DiscrepancyAssignmentEmailFactory> _logger;

    public DiscrepancyAssignmentEmailFactory(
        IEmailViewRenderer viewRenderer,
        IEmailBaseUrlProvider baseUrlProvider,
        ILogger<DiscrepancyAssignmentEmailFactory> logger)
    {
        _viewRenderer = viewRenderer;
        _baseUrlProvider = baseUrlProvider;
        _logger = logger;
    }

    /// <summary>Builds the assignment envelope. <paramref name="discrepancyId"/> drives the CTA
    /// (<c>/Reconciliation/{id}</c>); the amounts + participant are shown in the detail card.</summary>
    public async Task<EmailMessage> BuildAsync(
        string toAddress,
        string? assigneeFirstName,
        int discrepancyId,
        string applicationNumber,
        string participantName,
        string comparisonLabel,
        string severityLabel,
        decimal difference,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);

        var crc = new CultureInfo("es-CR");
        var baseUrl = _baseUrlProvider.GetBaseUrl();
        var ctaUrl = $"{baseUrl.TrimEnd('/')}/Reconciliation/{discrepancyId.ToString(CultureInfo.InvariantCulture)}";

        var model = new DirectEmailModel(
            Subject: Subject,
            HeroTitle: "Diferencia asignada",
            DisplayName: assigneeFirstName ?? string.Empty,
            Paragraphs: new[]
            {
                "Se le asignó una diferencia de reconciliación para su revisión y corrección.",
                "Abra el detalle para ver el historial, los montos y la acción requerida.",
            },
            CtaUrl: ctaUrl,
            CtaLabel: "Ver la diferencia",
            CardHeading: "Resumen de la diferencia",
            CardRows: new[]
            {
                new DetailRow("Participante", $"{applicationNumber} · {participantName}"),
                new DetailRow("Comparación", comparisonLabel),
                new DetailRow("Severidad", severityLabel),
                new DetailRow("Diferencia", Math.Abs(difference).ToString("C2", crc)),
            },
            FooterNote: null,
            LogoUrl: BrandAssets.LogoUrl(baseUrl),
            PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

        var html = await _viewRenderer.RenderViewAsync(HtmlView, model, disableLayout: false, ct);
        var text = await _viewRenderer.RenderViewAsync(TextView, model, disableLayout: true, ct);
        _logger.LogDebug("Built branded discrepancy-assignment email for {To}.", toAddress);
        return new EmailMessage(toAddress, Subject, html, text);
    }
}
