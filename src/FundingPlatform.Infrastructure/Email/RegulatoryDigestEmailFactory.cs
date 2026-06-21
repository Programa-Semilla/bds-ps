using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>Spec 043 / US4 — one affected (application, provider, field) row for the digest.</summary>
public sealed record RegulatoryDigestLine(string ApplicationCode, string SupplierName, RegulatoryField Field);

/// <summary>
/// Spec 043 / US4 — composes the daily stale-regulatory-value digest through the
/// spec-041 branded <c>_EmailLayout</c> shell (mirrors <c>StageReminderEmailFactory</c> /
/// <c>ProviderCreatedNotifier</c>). Returns a Notifications <see cref="EmailMessage"/> so the
/// allowlist-wrapped sender governs delivery.
/// </summary>
public sealed class RegulatoryDigestEmailFactory
{
    private const string HtmlView = "~/Views/Emails/Regulatory/StaleDigest.cshtml";
    private const string TextView = "~/Views/Emails/Regulatory/StaleDigest.text.cshtml";

    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IEmailBaseUrlProvider _baseUrlProvider;

    public RegulatoryDigestEmailFactory(IEmailViewRenderer viewRenderer, IEmailBaseUrlProvider baseUrlProvider)
    {
        _viewRenderer = viewRenderer;
        _baseUrlProvider = baseUrlProvider;
    }

    public async Task<EmailMessage> BuildAsync(
        string toEmail, string firstName, IReadOnlyList<RegulatoryDigestLine> lines, CancellationToken ct)
    {
        var baseUrl = _baseUrlProvider.GetBaseUrl();
        var cardRows = lines
            .Select(l => new DetailRow(l.ApplicationCode, $"{l.SupplierName} — {RegulatoryFreshnessCopy.FieldLabel(l.Field)}"))
            .ToArray();

        var model = new DirectEmailModel(
            Subject: RegulatoryFreshnessCopy.DigestSubject,
            HeroTitle: RegulatoryFreshnessCopy.DigestHeroTitle,
            DisplayName: firstName ?? string.Empty,
            Paragraphs: new[] { RegulatoryFreshnessCopy.DigestIntro },
            CtaUrl: $"{baseUrl}/Audit",
            CtaLabel: "Ir a auditoría",
            CardHeading: RegulatoryFreshnessCopy.DigestCardHeading,
            CardRows: cardRows,
            FooterNote: null,
            LogoUrl: BrandAssets.LogoUrl(baseUrl),
            PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

        var html = await _viewRenderer.RenderViewAsync(HtmlView, model, disableLayout: false, ct);
        var text = await _viewRenderer.RenderViewAsync(TextView, model, disableLayout: true, ct);

        return new EmailMessage(
            ToEmail: toEmail,
            ToDisplayName: string.IsNullOrWhiteSpace(firstName) ? toEmail : firstName,
            Subject: RegulatoryFreshnessCopy.DigestSubject,
            HtmlBody: html,
            TextBody: text,
            ReplyTo: null,
            Headers: null);
    }
}
