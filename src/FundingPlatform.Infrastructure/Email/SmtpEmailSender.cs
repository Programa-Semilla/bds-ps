// Spec 021 — see specs/021-feedback-session-may13/tasks.md (SMTP bootstrap
// under T117) and research.md R-11.

using System.Net;
using System.Net.Mail;
using FundingPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 021 / FR-025 — built-in <c>System.Net.Mail.SmtpClient</c>-backed
/// <see cref="IEmailSender"/>. Throws on transport failure so the reminder
/// service's exponential-backoff retry (NFR-002) can engage.
///
/// <para>NFR-005 — no new managed dependency. <c>System.Net.Mail.SmtpClient</c>
/// is marked obsolete-for-new-development by Microsoft, but it remains the
/// only built-in SMTP client in the BCL; we accept the obsolete warning here
/// because the alternative (MailKit) would add a NuGet package outside spec
/// scope.</para>
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            throw new InvalidOperationException(
                "SmtpEmailSender requires Smtp:Host to be configured. Fall back to LoggingEmailSender when SMTP is unavailable.");
        }

#pragma warning disable SYSLIB0014 // SmtpClient obsolete — accepted (NFR-005, no new deps).
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
        };
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };
        mail.To.Add(new MailAddress(message.ToAddress));

        _logger.LogInformation(
            "Sending SMTP email to {To} (subject={Subject}) via {Host}:{Port}.",
            message.ToAddress, message.Subject, _options.Host, _options.Port);

        await client.SendMailAsync(mail, ct).ConfigureAwait(false);
#pragma warning restore SYSLIB0014
    }
}
