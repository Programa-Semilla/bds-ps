// Spec 033 / FR-001 / C4 — set-password invitation email envelope.
// Mirrors ForgotPasswordEmailFactory: the .cshtml is read as a plain-text
// token file (not a Razor view) so composition happens outside the HTTP
// request scope, and {{TOKEN}} placeholders are substituted here.

using System.Globalization;
using FundingPlatform.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 033 / FR-001 — composes the es-CR set-password invitation email from
/// <c>src/FundingPlatform.Web/Views/Emails/Identity/InvitationEmail.cshtml</c>.
/// The admin creates the account with no password; this email carries the 72h
/// single-use set-password link to the new user. Direct-send (D5), not the
/// spec-021 outbox — structurally identical to the forgot-password path.
/// </summary>
public sealed class InvitationEmailFactory
{
    // Spec 033 / C4 — fixed es-CR subject.
    private const string Subject = "Le han creado una cuenta — establezca su contraseña";

    private readonly IHostEnvironment _env;
    private readonly ILogger<InvitationEmailFactory> _logger;
    private string? _cached;
    private readonly Lock _cacheLock = new();

    public InvitationEmailFactory(IHostEnvironment env, ILogger<InvitationEmailFactory> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Builds the invitation envelope. <paramref name="inviteLink"/> is the
    /// absolute <c>/Account/ResetPassword</c> URL embedded as
    /// <c>{{InviteLink}}</c>; <paramref name="expiresAt"/> is formatted in CR
    /// local time for <c>{{ExpiresAt}}</c>.
    /// </summary>
    public EmailMessage Build(
        string toAddress,
        string? firstName,
        string inviteLink,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteLink);

        var expiresAtLocal = expiresAt
            .ToOffset(TimeSpan.FromHours(-6)) // CR is UTC-6 (no DST)
            .ToString("dd/MM/yyyy HH:mm", new CultureInfo("es-CR"));

        // HTML-encode the only free-text token so an admin-entered name containing
        // markup cannot inject HTML into the email body. InviteLink/ExpiresAt are
        // system-generated and safe.
        var safeFirstName = System.Net.WebUtility.HtmlEncode(firstName ?? string.Empty);

        var template = ReadTemplate();
        var body = template
            .Replace("{{InviteLink}}", inviteLink, StringComparison.Ordinal)
            .Replace("{{FirstName}}", safeFirstName, StringComparison.Ordinal)
            .Replace("{{ExpiresAt}}", expiresAtLocal, StringComparison.Ordinal);

        return new EmailMessage(toAddress, Subject, body);
    }

    private string ReadTemplate()
    {
        lock (_cacheLock)
        {
            if (_cached is not null) return _cached;
        }

        const string fileName = "InvitationEmail.cshtml";
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
                _logger.LogDebug("Loaded invitation email template from {Path}.", full);
                return text;
            }
        }

        var fallback =
            "<p>Le han creado una cuenta en la plataforma. Abra el siguiente enlace para establecer su contraseña:</p>" +
            "<p><a href=\"{{InviteLink}}\">{{InviteLink}}</a></p>" +
            "<p>El enlace expira el {{ExpiresAt}}.</p>";
        // Cache the fallback too: the factory is a singleton, so without this a
        // genuinely missing template would re-probe the filesystem and re-log a
        // WARN on every send.
        lock (_cacheLock)
        {
            _cached ??= fallback;
        }
        _logger.LogWarning(
            "Invitation email template '{File}' not found under any of the candidate paths; falling back to minimal HTML body.",
            fileName);
        return fallback;
    }
}
