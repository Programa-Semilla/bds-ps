// Spec 021 — see specs/021-feedback-session-may13/tasks.md T118
// and contracts/public-routes.md (email subjects).

using System.Globalization;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 021 / T118 / FR-025 — composes the three reminder-email envelopes
/// (T-72h, T-24h, expiry) from the .cshtml templates under
/// <c>src/FundingPlatform.Web/Views/Emails/Stages/</c>.
///
/// <para>The templates are treated as plain-text token files (NOT Razor views)
/// because the reminder hosted service runs outside the HTTP request scope.
/// Razor rendering would require an HttpContext shim that violates NFR-005
/// (no new managed deps) for a three-template subset.</para>
///
/// <para>File contents are read once on first use and cached in-memory — the
/// templates ship in the Web project and do not change at runtime.</para>
/// </summary>
public sealed class StageReminderEmailFactory
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<StageReminderEmailFactory> _logger;
    private readonly Dictionary<ReminderBucket, string> _cache = [];
    private readonly Lock _cacheLock = new();

    public StageReminderEmailFactory(IHostEnvironment env, ILogger<StageReminderEmailFactory> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Builds the envelope for <paramref name="bucket"/>. Throws
    /// <see cref="InvalidOperationException"/> if the template file cannot be
    /// located (deployment regression).
    /// </summary>
    public EmailMessage Build(
        ReminderBucket bucket,
        string toAddress,
        string applicantFirstName,
        string publicCode,
        StageKind stage,
        DateTimeOffset closesAt)
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

        // Templates are read as plain text — strip Razor @* … *@ comments so a
        // header comment (and any {{token}} inside it) does not leak into the body.
        var template = EmailTemplateText.StripRazorComments(ReadTemplate(bucket));
        var body = template
            .Replace("{{PublicCode}}", publicCode, StringComparison.Ordinal)
            .Replace("{{StageName}}", stageLabel, StringComparison.Ordinal)
            .Replace("{{ClosesAtLocal}}", closesAtLocal, StringComparison.Ordinal)
            .Replace("{{ApplicantName}}", applicantFirstName ?? string.Empty, StringComparison.Ordinal);

        var subject = bucket switch
        {
            ReminderBucket.T72h => $"Su solicitud {publicCode} cierra en 72 horas",
            ReminderBucket.T24h => $"Su solicitud {publicCode} cierra en 24 horas",
            ReminderBucket.Expired => $"La etapa de {publicCode} cerró el {closesAtLocal}",
            _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
        };

        return new EmailMessage(toAddress, subject, body);
    }

    private static string StageLabel(StageKind stage) => stage switch
    {
        StageKind.Solicitud => "Solicitud",
        StageKind.Revision => "Revisión",
        StageKind.Facturacion => "Facturación",
        _ => stage.ToString(),
    };

    private string ReadTemplate(ReminderBucket bucket)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(bucket, out var cached))
            {
                return cached;
            }
        }

        var fileName = bucket switch
        {
            ReminderBucket.T72h => "T72ReminderEmail.cshtml",
            ReminderBucket.T24h => "T24ReminderEmail.cshtml",
            ReminderBucket.Expired => "ExpiredEmail.cshtml",
            _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
        };

        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, "Views", "Emails", "Stages", fileName),
            // Repo-root layout (used by integration tests that don't run from the Web ContentRoot).
            Path.Combine(_env.ContentRootPath, "..", "FundingPlatform.Web", "Views", "Emails", "Stages", fileName),
            Path.Combine(_env.ContentRootPath, "..", "..", "src", "FundingPlatform.Web", "Views", "Emails", "Stages", fileName),
            Path.Combine(_env.ContentRootPath, "..", "..", "..", "src", "FundingPlatform.Web", "Views", "Emails", "Stages", fileName),
        };

        foreach (var path in candidates)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                var text = File.ReadAllText(full);
                lock (_cacheLock)
                {
                    _cache[bucket] = text;
                }
                _logger.LogDebug("Loaded stage reminder email template '{File}' from {Path}.", fileName, full);
                return text;
            }
        }

        // Fallback — inline body so dev never crashes the hosted service over a
        // missing file. The integration test asserts the real template path is
        // used in production layout (content root = Web project).
        var fallback = $"<p>Solicitud {{{{PublicCode}}}} — recordatorio (template '{fileName}' no encontrado).</p>";
        _logger.LogWarning(
            "Stage reminder email template '{File}' not found under any of the candidate paths; falling back to minimal HTML body.",
            fileName);
        return fallback;
    }
}
