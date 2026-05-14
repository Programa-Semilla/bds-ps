// Spec 021 — see specs/021-feedback-session-may13/tasks.md T129
// and contracts/public-routes.md (email subjects).

using System.Globalization;
using FundingPlatform.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 021 / T129 / FR-028 — composes the password-reset email envelope from
/// <c>src/FundingPlatform.Web/Views/Emails/Identity/ForgotPasswordEmail.cshtml</c>.
///
/// <para>The template is treated as a plain-text token file (NOT a Razor view)
/// because email composition happens outside of the HTTP request scope of a
/// rendered view. Mirrors the
/// <see cref="StageReminderEmailFactory"/> approach for consistency and
/// to avoid pulling Razor view-rendering into the email path.</para>
/// </summary>
public sealed class ForgotPasswordEmailFactory
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<ForgotPasswordEmailFactory> _logger;
    private string? _cached;
    private readonly Lock _cacheLock = new();

    public ForgotPasswordEmailFactory(IHostEnvironment env, ILogger<ForgotPasswordEmailFactory> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Builds the password-reset envelope. <paramref name="resetLink"/> is the
    /// absolute URL embedded as <c>{{ResetLink}}</c>; <paramref name="expiresAt"/>
    /// is formatted in es-CR local time for <c>{{ExpiresAt}}</c>.
    /// </summary>
    public EmailMessage Build(
        string toAddress,
        string? applicantFirstName,
        string resetLink,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(resetLink);

        var expiresAtLocal = expiresAt
            .ToOffset(TimeSpan.FromHours(-6)) // CR is UTC-6 (no DST)
            .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));

        var template = ReadTemplate();
        var body = template
            .Replace("{{ResetLink}}", resetLink, StringComparison.Ordinal)
            .Replace("{{ApplicantName}}", applicantFirstName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{{ExpiresAt}}", expiresAtLocal, StringComparison.Ordinal);

        // FR-028 / contracts/public-routes.md — subject line is fixed.
        const string subject = "Restablezca su contraseña";
        return new EmailMessage(toAddress, subject, body);
    }

    private string ReadTemplate()
    {
        lock (_cacheLock)
        {
            if (_cached is not null) return _cached;
        }

        const string fileName = "ForgotPasswordEmail.cshtml";
        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, "Views", "Emails", "Identity", fileName),
            // Repo-root layout used by integration tests that don't run from the Web ContentRoot.
            Path.Combine(_env.ContentRootPath, "..", "FundingPlatform.Web", "Views", "Emails", "Identity", fileName),
            Path.Combine(_env.ContentRootPath, "..", "..", "src", "FundingPlatform.Web", "Views", "Emails", "Identity", fileName),
            Path.Combine(_env.ContentRootPath, "..", "..", "..", "src", "FundingPlatform.Web", "Views", "Emails", "Identity", fileName),
        };

        foreach (var path in candidates)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                var text = File.ReadAllText(full);
                lock (_cacheLock)
                {
                    _cached = text;
                }
                _logger.LogDebug("Loaded forgot-password email template from {Path}.", full);
                return text;
            }
        }

        var fallback =
            "<p>Solicitud de restablecimiento de contraseña — abra el siguiente enlace:</p>" +
            "<p><a href=\"{{ResetLink}}\">{{ResetLink}}</a></p>" +
            "<p>El enlace expira el {{ExpiresAt}}.</p>";
        _logger.LogWarning(
            "Forgot-password email template '{File}' not found under any of the candidate paths; falling back to minimal HTML body.",
            fileName);
        return fallback;
    }
}
