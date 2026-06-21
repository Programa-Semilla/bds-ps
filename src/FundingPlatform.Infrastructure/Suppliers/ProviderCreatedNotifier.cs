// Spec 038 (US4) — see specs/038-auditor-provider-compliance/contracts/interfaces.md
// (IProviderCreatedNotifier) and research.md D11.

using System.Globalization;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Application.Suppliers.Notifications;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Suppliers;

/// <summary>
/// Spec 038 (US4) + Spec 041 / T019 — emails every Auditor when a provider is
/// created. Sends one message per auditor through the allowlist-wrapped
/// Notifications <see cref="IEmailSender"/> (NOT the direct-send Abstractions path,
/// which is not allowlisted — D11). Best-effort: any failure is caught + logged and
/// never propagates to the creation flow (FR-024).
///
/// <para>Spec 041 routes the body through the shared branded <c>_EmailLayout</c>
/// via <see cref="IEmailViewRenderer"/> (Decision 1) instead of plain-text token
/// substitution; the provider detail renders in a "Detalle" card.</para>
/// </summary>
public sealed class ProviderCreatedNotifier : IProviderCreatedNotifier
{
    private const string HtmlView = "~/Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml";
    private const string TextView = "~/Views/Emails/Suppliers/ProviderCreatedAuditor.text.cshtml";

    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IEmailBaseUrlProvider _baseUrlProvider;
    private readonly ILogger<ProviderCreatedNotifier> _logger;

    public ProviderCreatedNotifier(
        AppDbContext db,
        IEmailSender emailSender,
        IEmailViewRenderer viewRenderer,
        IEmailBaseUrlProvider baseUrlProvider,
        ILogger<ProviderCreatedNotifier> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _viewRenderer = viewRenderer;
        _baseUrlProvider = baseUrlProvider;
        _logger = logger;
    }

    public async Task NotifyAuditorsAsync(int supplierId, CancellationToken ct)
    {
        try
        {
            var supplier = await _db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == supplierId, ct);
            if (supplier is null)
                return;

            var auditors = await (
                from ur in _db.UserRoles
                join r in _db.Roles on ur.RoleId equals r.Id
                join u in _db.Users on ur.UserId equals u.Id
                where r.NormalizedName == "AUDITOR" && u.Email != null
                select new { u.Email, u.FirstName, u.LastName })
                .ToListAsync(ct);
            if (auditors.Count == 0)
                return;

            string? creatorName = null;
            if (supplier.CreatedByApplicantId is int applicantId)
            {
                creatorName = await _db.Applicants
                    .Where(a => a.Id == applicantId)
                    .Select(a => (a.FirstName + " " + a.LastName).Trim())
                    .FirstOrDefaultAsync(ct);
            }
            creatorName = string.IsNullOrWhiteSpace(creatorName) ? "—" : creatorName;

            var baseUrl = _baseUrlProvider.GetBaseUrl();
            var reviewLink = $"{baseUrl}/Admin/Suppliers/{supplier.Id}";
            var createdAtLocal = supplier.CreatedAt
                .AddHours(-6) // CR is UTC-6 (no DST)
                .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));
            var subject = $"Nuevo proveedor para revisar: {supplier.Name}";

            // Spec 041 / T019 — branded "Detalle del proveedor" card; the renderer
            // HTML-encodes the values, so pass them raw (no manual HtmlEncode).
            var cardRows = new[]
            {
                new DetailRow("Proveedor", supplier.Name),
                new DetailRow("Cédula jurídica", supplier.LegalId),
                new DetailRow("Fecha de registro", createdAtLocal),
                new DetailRow("Creado por", creatorName),
            };
            var paragraphs = new[]
            {
                "Te informamos que se registró una nueva empresa proveedora en ALIA que requiere revisión regulatoria.",
                "Revisá el cumplimiento regulatorio (Hacienda, CCSS, SICOP) del proveedor desde la plataforma.",
            };

            var sent = 0;
            var blocked = 0;
            foreach (var auditor in auditors)
            {
                var displayName = $"{auditor.FirstName} {auditor.LastName}".Trim();
                var model = new DirectEmailModel(
                    Subject: subject,
                    HeroTitle: "Nuevo proveedor registrado",
                    DisplayName: auditor.FirstName ?? string.Empty,
                    Paragraphs: paragraphs,
                    CtaUrl: reviewLink,
                    CtaLabel: "Revisar proveedor",
                    CardHeading: "Detalle del proveedor",
                    CardRows: cardRows,
                    FooterNote: null,
                    LogoUrl: BrandAssets.LogoUrl(baseUrl),
                    PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

                var htmlBody = await _viewRenderer.RenderViewAsync(HtmlView, model, disableLayout: false, ct);
                var textBody = await _viewRenderer.RenderViewAsync(TextView, model, disableLayout: true, ct);

                var message = new EmailMessage(
                    ToEmail: auditor.Email!,
                    ToDisplayName: displayName,
                    Subject: subject,
                    HtmlBody: htmlBody,
                    TextBody: textBody,
                    ReplyTo: null,
                    Headers: null);

                var result = await _emailSender.SendAsync(message, ct);
                switch (result.Outcome)
                {
                    case EmailSendOutcome.Sent:
                        sent++;
                        break;
                    case EmailSendOutcome.BlockedByAllowlist:
                        blocked++;
                        _logger.LogInformation(
                            "Provider-created notification to auditor {Email} for supplier {SupplierId} was dropped by the recipient allowlist.",
                            auditor.Email, supplier.Id);
                        break;
                    default:
                        _logger.LogWarning(
                            "Provider-created notification to auditor {Email} for supplier {SupplierId} returned {Outcome}: {Error}",
                            auditor.Email, supplier.Id, result.Outcome, result.ErrorMessage);
                        break;
                }
            }

            _logger.LogInformation(
                "Provider-created notification for supplier {SupplierId}: {Sent} sent, {Blocked} blocked by allowlist, of {Total} auditors.",
                supplier.Id, sent, blocked, auditors.Count);
        }
        catch (Exception ex)
        {
            // FR-024 — best-effort: never block provider creation.
            _logger.LogWarning(ex,
                "Provider-created auditor notification failed for supplier {SupplierId}.", supplierId);
        }
    }
}
