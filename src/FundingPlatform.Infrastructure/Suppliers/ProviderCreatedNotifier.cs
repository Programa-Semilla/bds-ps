// Spec 038 (US4) — see specs/038-auditor-provider-compliance/contracts/interfaces.md
// (IProviderCreatedNotifier) and research.md D11.

using System.Globalization;
using FundingPlatform.Application.Notifications;
using FundingPlatform.Application.Suppliers.Notifications;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Suppliers;

/// <summary>
/// Spec 038 (US4) — emails every Auditor when a provider is created. Sends one
/// message per auditor through the allowlist-wrapped Notifications
/// <see cref="IEmailSender"/> (NOT the direct-send Abstractions path, which is not
/// allowlisted — D11). Best-effort: any failure is caught + logged and never
/// propagates to the creation flow (FR-024).
/// </summary>
public sealed class ProviderCreatedNotifier : IProviderCreatedNotifier
{
    private const string TemplateFile = "ProviderCreatedAuditor.cshtml";

    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ProviderCreatedNotifier> _logger;
    private string? _cachedTemplate;

    public ProviderCreatedNotifier(
        AppDbContext db,
        IEmailSender emailSender,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<ProviderCreatedNotifier> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _config = config;
        _env = env;
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

            var baseUrl = (_config["Notifications:BaseUrl"] ?? string.Empty).TrimEnd('/');
            var reviewLink = $"{baseUrl}/Admin/Suppliers/{supplier.Id}";
            var createdAtLocal = supplier.CreatedAt
                .AddHours(-6) // CR is UTC-6 (no DST)
                .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));
            var subject = $"Nuevo proveedor para revisar: {supplier.Name}";

            var template = EmailTemplateText.StripRazorComments(ReadTemplate());
            var htmlBody = template
                .Replace("{{ProviderName}}", System.Net.WebUtility.HtmlEncode(supplier.Name), StringComparison.Ordinal)
                .Replace("{{ProviderLegalId}}", System.Net.WebUtility.HtmlEncode(supplier.LegalId), StringComparison.Ordinal)
                .Replace("{{CreatedAt}}", createdAtLocal, StringComparison.Ordinal)
                .Replace("{{CreatedByName}}", System.Net.WebUtility.HtmlEncode(creatorName), StringComparison.Ordinal)
                .Replace("{{ReviewLink}}", reviewLink, StringComparison.Ordinal);

            var textBody =
                $"Nuevo proveedor para revisar: {supplier.Name}\n" +
                $"Cédula jurídica: {supplier.LegalId}\n" +
                $"Creado: {createdAtLocal}\n" +
                $"Creado por: {creatorName}\n" +
                $"Revisar: {reviewLink}";

            foreach (var auditor in auditors)
            {
                var displayName = $"{auditor.FirstName} {auditor.LastName}".Trim();
                var message = new EmailMessage(
                    ToEmail: auditor.Email!,
                    ToDisplayName: displayName,
                    Subject: subject,
                    HtmlBody: htmlBody,
                    TextBody: textBody,
                    ReplyTo: null,
                    Headers: null);

                var result = await _emailSender.SendAsync(message, ct);
                if (result.Outcome is EmailSendOutcome.TransientFailure or EmailSendOutcome.PermanentFailure)
                {
                    _logger.LogWarning(
                        "Provider-created notification to auditor {Email} for supplier {SupplierId} returned {Outcome}: {Error}",
                        auditor.Email, supplier.Id, result.Outcome, result.ErrorMessage);
                }
            }
        }
        catch (Exception ex)
        {
            // FR-024 — best-effort: never block provider creation.
            _logger.LogWarning(ex,
                "Provider-created auditor notification failed for supplier {SupplierId}.", supplierId);
        }
    }

    private string ReadTemplate()
    {
        if (_cachedTemplate is not null)
            return _cachedTemplate;

        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, "Views", "Emails", "Suppliers", TemplateFile),
            Path.Combine(_env.ContentRootPath, "..", "FundingPlatform.Web", "Views", "Emails", "Suppliers", TemplateFile),
            Path.Combine(_env.ContentRootPath, "..", "..", "src", "FundingPlatform.Web", "Views", "Emails", "Suppliers", TemplateFile),
            Path.Combine(_env.ContentRootPath, "..", "..", "..", "src", "FundingPlatform.Web", "Views", "Emails", "Suppliers", TemplateFile),
        };

        foreach (var path in candidates)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                _cachedTemplate = File.ReadAllText(full);
                return _cachedTemplate;
            }
        }

        _logger.LogWarning(
            "Provider-created email template '{File}' not found; using minimal fallback body.", TemplateFile);
        _cachedTemplate =
            "<p>Nuevo proveedor para revisar: {{ProviderName}} ({{ProviderLegalId}}).</p>" +
            "<p>Creado el {{CreatedAt}} por {{CreatedByName}}.</p>" +
            "<p><a href=\"{{ReviewLink}}\">{{ReviewLink}}</a></p>";
        return _cachedTemplate;
    }
}
