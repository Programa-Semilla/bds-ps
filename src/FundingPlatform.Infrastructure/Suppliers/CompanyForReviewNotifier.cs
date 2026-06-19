// Spec 041 / US4 / T035 / FR-013 — "Nueva empresa para revisión" notifier stub.
// Mirrors ProviderCreatedNotifier (Application interface / Infrastructure impl).
// DEFERRED (OQ-1): no call site invokes NotifyAsync; the recipient pool + live
// trigger are unconfirmed. The body renders the branded template (so the seam is
// proven render-only); once OQ-1 lands, resolve recipients + send here.

using System.Globalization;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Application.Suppliers.Notifications;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Suppliers;

/// <summary>
/// Spec 041 / US4 — render-only stub for the branded "nueva empresa para revisión"
/// auditor/reviewer email (reference copy #9). Builds the branded
/// <see cref="DirectEmailModel"/> with a populated "Detalle de la empresa" card and
/// renders it through the shared <c>_EmailLayout</c> via <see cref="IEmailViewRenderer"/>,
/// proving the template + seam work. Delivery (recipient pool + live trigger) is
/// DEFERRED to OQ-1; no call site invokes this notifier yet.
/// </summary>
public sealed class CompanyForReviewNotifier : ICompanyForReviewNotifier
{
    private const string HtmlView = "~/Views/Emails/Suppliers/CompanyForReviewAuditor.cshtml";
    private const string TextView = "~/Views/Emails/Suppliers/CompanyForReviewAuditor.text.cshtml";

    private readonly AppDbContext _db;
    private readonly IEmailViewRenderer _viewRenderer;
    private readonly IConfiguration _config;
    private readonly ILogger<CompanyForReviewNotifier> _logger;

    public CompanyForReviewNotifier(
        AppDbContext db,
        IEmailViewRenderer viewRenderer,
        IConfiguration config,
        ILogger<CompanyForReviewNotifier> logger)
    {
        _db = db;
        _viewRenderer = viewRenderer;
        _config = config;
        _logger = logger;
    }

    public async Task NotifyAsync(int companyId, CancellationToken ct)
    {
        try
        {
            var company = await _db.Companies.AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => new { c.Id, c.Name, c.CreatedAt, c.ApplicantId })
                .FirstOrDefaultAsync(ct);
            if (company is null)
                return;

            var applicant = await _db.Applicants.AsNoTracking()
                .Where(a => a.Id == company.ApplicantId)
                .Select(a => new { a.FirstName, a.LastName, a.LegalId })
                .FirstOrDefaultAsync(ct);

            var applicantName = applicant is null
                ? "—"
                : $"{applicant.FirstName} {applicant.LastName}".Trim();
            var createdLocal = company.CreatedAt
                .AddHours(-6) // CR is UTC-6 (no DST)
                .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));

            var baseUrl = _config["Notifications:BaseUrl"];
            var model = new DirectEmailModel(
                Subject: $"Nueva empresa para revisión: {company.Name}",
                HeroTitle: "Nueva empresa para revisión",
                DisplayName: string.Empty, // OQ-1 — recipient identity TBD.
                Paragraphs: new[]
                {
                    "Te informamos que una nueva empresa fue ingresada en ALIA y se encuentra disponible para revisión según el proceso correspondiente.",
                    "Podés ingresar a la plataforma para consultar la información registrada y dar seguimiento según tu perfil.",
                },
                CtaUrl: null,   // FR-005 — review route deferred (OQ-1); no URL invented.
                CtaLabel: null,
                CardHeading: "Detalle de la empresa",
                CardRows: new[]
                {
                    new DetailRow("Empresa", company.Name),
                    new DetailRow("Solicitante", applicantName),
                    new DetailRow("Identificación del solicitante", applicant?.LegalId ?? "—"),
                    new DetailRow("Fecha de ingreso", createdLocal),
                },
                FooterNote: null,
                LogoUrl: BrandAssets.LogoUrl(baseUrl),
                PartnerStripUrl: BrandAssets.PartnerStripUrl(baseUrl));

            // Render the branded HTML + text (proves the template + seam work).
            _ = await _viewRenderer.RenderViewAsync(HtmlView, model, disableLayout: false, ct);
            _ = await _viewRenderer.RenderViewAsync(TextView, model, disableLayout: true, ct);

            // OQ-1 — DEFERRED: resolve the recipient pool (reviewers vs auditors) and
            // send the rendered bodies via the allowlist-wrapped Notifications
            // IEmailSender here, mirroring ProviderCreatedNotifier. No recipients are
            // resolved yet and no call site triggers this notifier.
            _logger.LogInformation(
                "Company-for-review email rendered for company {CompanyId}; live delivery deferred (OQ-1).",
                companyId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Company-for-review notification (render-only stub) failed for company {CompanyId}.", companyId);
        }
    }
}
