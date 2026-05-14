// Spec 021 — fallback IEmailSender for environments without SMTP configured.

using FundingPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.Email;

/// <summary>
/// Spec 021 / FR-025 — dev fallback <see cref="IEmailSender"/> used when
/// <c>Smtp:Host</c> is unset. Writes the envelope to <see cref="ILogger"/>
/// at <c>Information</c> level so the message body is still observable in
/// dev logs / E2E test traces without requiring a working SMTP server.
///
/// <para>Does NOT throw on send — the local "transport" is the logger; the
/// reminder service treats this as a successful send and sets the
/// corresponding bit in <c>RemindersSentMask</c>.</para>
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _logger.LogInformation(
            "[LoggingEmailSender] To={To} Subject=\"{Subject}\" Body.Length={Length}",
            message.ToAddress, message.Subject, message.HtmlBody.Length);
        return Task.CompletedTask;
    }
}
