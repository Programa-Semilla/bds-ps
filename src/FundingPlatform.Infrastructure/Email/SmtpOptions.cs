// Spec 021 — see specs/021-feedback-session-may13/research.md R-11
// and contracts/public-routes.md (email subjects).

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 021 / FR-025 — SMTP transport configuration. Read from configuration
/// section <c>Smtp:*</c>; missing/empty <see cref="Host"/> swaps the binding
/// to <c>LoggingEmailSender</c> so dev runs don't fail.
///
/// <para>NFR-005 — uses <c>System.Net.Mail.SmtpClient</c> (built-in). No new
/// managed dependencies (MailKit was considered and rejected because the
/// scope is "send three reminder emails per cycle" — built-in is sufficient).</para>
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>SMTP server hostname (e.g. <c>smtp.sendgrid.net</c>).</summary>
    public string? Host { get; set; }

    /// <summary>SMTP port. Defaults to 587 (STARTTLS submission).</summary>
    public int Port { get; set; } = 587;

    /// <summary>SMTP auth user (nullable for unauthenticated relays).</summary>
    public string? Username { get; set; }

    /// <summary>SMTP auth password (nullable for unauthenticated relays).</summary>
    public string? Password { get; set; }

    /// <summary>From-address header on every outbound message.</summary>
    public string FromAddress { get; set; } = "no-reply@programa-semilla.cr";

    /// <summary>Toggle STARTTLS/SSL. Defaults to true (NFR-aligned).</summary>
    public bool UseSsl { get; set; } = true;
}
